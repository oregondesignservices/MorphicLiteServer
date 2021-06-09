// Copyright 2020 Raising the Floor - International
//
// Licensed under the New BSD license. You may not use this file except in
// compliance with this License.
//
// You may obtain a copy of the License at
// https://github.com/GPII/universal/blob/master/LICENSE.txt
//
// The R&D leading to these results received funding from the:
// * Rehabilitation Services Administration, US Dept. of Education under 
//   grant H421A150006 (APCP)
// * National Institute on Disability, Independent Living, and 
//   Rehabilitation Research (NIDILRR)
// * Administration for Independent Living & Dept. of Education under grants 
//   H133E080022 (RERC-IT) and H133E130028/90RE5003-01-00 (UIITA-RERC)
// * European Union's Seventh Framework Programme (FP7/2007-2013) grant 
//   agreement nos. 289016 (Cloud4all) and 610510 (Prosperity4All)
// * William and Flora Hewlett Foundation
// * Ontario Ministry of Research and Innovation
// * Canadian Foundation for Innovation
// * Adobe Foundation
// * Consumer Electronics Association Foundation
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Text;
using Hangfire;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using Morphic.Server.Email;
using Stripe;

namespace Morphic.Server.Community
{

    using Http;
    using Users;
    using Billing;

    [Path("/v1/communities")]
    public class CommunitiesEndpoint: Endpoint
    {

        public CommunitiesEndpoint(IPaymentProcessor paymentProcessor, Plans plans,
            IHttpContextAccessor contextAccessor, ILogger<Endpoint> logger, IBackgroundJobClient jobClient)
            : base(contextAccessor, logger)
        {
            this.paymentProcessor = paymentProcessor;
            this.plans = plans;
            this.jobClient = jobClient;
        }

        public override async Task LoadResource()
        {
            User = await RequireUser();
        }

        private User User = null!;
        private IPaymentProcessor paymentProcessor;
        private Plans plans;
        private readonly IBackgroundJobClient jobClient;

        [Method]
        public async Task Post()
        {
            var db = Context.GetDatabase();
            var input = await Request.ReadJson<CommunityPutRequest>();
            var communityId = Guid.NewGuid().ToString();

            var populateDefaultBars = Request.Query.ContainsKey("populate_default_bars");

            var bar = new Bar()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Default",
                CommunityId = communityId,
                CreatedAt = DateTime.Now,
                IsShared = true
            };
            if (populateDefaultBars)
            {
                bar.Items = new BarItem[]{
                    new BarItem(){
                        Kind = BarItemKind.Link,
                        Configuration = new Dictionary<string, object?>(){
                            { "label", "Email" },
                            { "image_url", "envelope-solid" },
                            { "url", "https://mail.google.com" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Link,
                        Configuration = new Dictionary<string, object?>(){
                            { "label", "Calendar" },
                            { "image_url", "calendar-solid" },
                            { "url", "https://calendar.google.com" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Link,
                        Configuration = new Dictionary<string, object?>(){
                            { "label", "Amazon" },
                            { "image_url", "amazon-brands" },
                            { "url", "https://amazon.com" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Link,
                        Configuration = new Dictionary<string, object?>(){
                            { "label", "CNN" },
                            { "image_url", "newspaper-solid" },
                            { "url", "https://cnn.com" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Action,
                        Configuration = new Dictionary<string, object?>(){
                            { "identifier", "screen-zoom" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Link,
                        Configuration = new Dictionary<string, object?>(){
                            { "subkind", "skype" },
                            { "label", "Family Call" },
                            { "image_url", "video-solid" },
                            { "url", "https://join.skype.com/" },
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Action,
                        Configuration = new Dictionary<string, object?>(){
                            { "identifier", "magnify" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Action,
                        Configuration = new Dictionary<string, object?>(){
                            { "identifier", "volume" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Application,
                        Configuration = new Dictionary<string, object?>(){
                            { "label", "Quick Assist" },
                            { "image_url", "question-solid" },
                            { "default", "quick-assist" }
                        }
                    },
                    new BarItem(){
                        Kind = BarItemKind.Action,
                        Configuration = new Dictionary<string, object?>(){
                            { "identifier", "copy-paste" }
                        }
                    },
                };
            }
            await db.Save(bar);

            var plan = plans.Default;

            var memberId = Guid.NewGuid().ToString();

            var billing = new BillingRecord()
            {
                Id = Guid.NewGuid().ToString(),
                CommunityId = communityId,
                PlanId = plan.Id,
                // Trial if for 30 days, round it up to the end of the day.
                TrialEnd = DateTime.Now.Date.AddDays(31).AddMinutes(-1),
                Status = BillingStatus.Paid,
                ContactMemeberId = memberId
            };

            var community = new Community()
            {
                Id = communityId,
                Name = input.Name,
                DefaultBarId = bar.Id,
                CreatedAt = DateTime.Now,
                BillingId = billing.Id,
                MemberCount = 0,
                MemberLimit = plan.MemberLimit
            };
            await db.Save(community);

            await paymentProcessor.StartCommunitySubscription(community, billing, User);

            await db.Save(billing);

            // Everyone gets one free user, so we don't count this first one towards community.MemberCount
            var member = new Member()
            {
                Id = memberId,
                CommunityId = community.Id,
                UserId = User.Id,
                Role = MemberRole.Manager,
                State = MemberState.Active,
                CreatedAt = DateTime.Now
            };
            member.FirstName.PlainText = User.FirstName?.PlainText;
            member.LastName.PlainText = User.LastName?.PlainText;
            await db.Save(member);


            // Send an email alert when someone signs up
            StringBuilder emailInfo = new StringBuilder();
            JsonWriterSettings writerSettings = new JsonWriterSettings()
            {
                Indent = true
            };

            emailInfo.AppendFormat("Community '{0}' created by '{1}' {2}",
                community.Name, User.FullName, User.Email.PlainText).AppendLine();

            emailInfo.AppendLine("Member:").AppendLine(member.ToJson(writerSettings));
            emailInfo.AppendLine("Community:").AppendLine(community.ToJson(writerSettings));

            jobClient.Enqueue<InternalAlertEmail>(x =>
                x.SendEmail("new community", emailInfo.ToString(), this.Request.ClientIp()));


            await Respond(new CommunityPutResponse()
            {
                Community = community
            });
        }

        class CommunityPutRequest
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = null!;
        }

        class CommunityPutResponse
        {
            [JsonPropertyName("community")]
            public Community Community { get; set; } = null!;
        }
    }

}