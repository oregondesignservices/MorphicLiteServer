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

using System.Collections.Generic;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Morphic.Server.Auth
{

    using Http;

    [Path("/v1/users/{userid}/password")]
    public class ChangePasswordEndpoint : Endpoint
    {
        private IBackgroundJobClient jobClient;
        
        public ChangePasswordEndpoint(
            IHttpContextAccessor contextAccessor,
            ILogger<ChangePasswordEndpoint> logger,
            IBackgroundJobClient jobClient): base(contextAccessor, logger)
        {
            this.jobClient = jobClient;
        }

        /// <summary>The user id to use, populated from the request URL</summary>
        [Parameter]
        public string UserId = "";

        public override async Task LoadResource()
        {
            var authenticatedUser = await RequireUser();
            if (authenticatedUser.Id != UserId)
            {
                throw new HttpError(HttpStatusCode.Forbidden);
            }
            
            usernameCredentials = await Load<UsernameCredential>(u => u.UserId == authenticatedUser.Id);
            logger.LogDebug("Loaded user credential for {UserId}", authenticatedUser.Id);
        }

        /// <summary>The UsernameCredential data populated by <code>LoadResource()</code></summary>
        private UsernameCredential usernameCredentials = null!;

        [Method]
        public async Task Post()
        {
            var request = await Request.ReadJson<ChangePasswordRequest>();
            if (request.NewPassword == "")
            {
                throw new HttpError(HttpStatusCode.BadRequest, BadPasswordChangeResponse.MissingRequired);
            }
            var db = Context.GetDatabase();
            var user = await db.UserForUsernameCredential(usernameCredentials, request.ExistingPassword);
            usernameCredentials.CheckAndSetPassword(request.NewPassword);
            if (request.DeleteExistingTokens)
            {
                await Delete<AuthToken>(token => token.UserId == user.Id);
            }
            await Save(usernameCredentials);
            jobClient.Enqueue<ChangePasswordEmail>(x => x.SendEmail(
                user.Id,
                Request.ClientIp()
            ));

        }

        /// <summary>Model for change password requests</summary>
        public class ChangePasswordRequest
        {
            [JsonPropertyName("existing_password")]
            public string ExistingPassword { get; set; } = null!;
            [JsonPropertyName("new_password")]
            public string NewPassword { get; set; } = null!;
            [JsonPropertyName("delete_existing_tokens")]
            public bool DeleteExistingTokens { get; set; } = false;
        }

        public class BadPasswordChangeResponse : BadRequestResponse
        {
            public static readonly BadPasswordChangeResponse MissingRequired = new BadPasswordChangeResponse(
                "missing_required",
                new Dictionary<string, object>
                {
                    {"required", new List<string> {"new_password"}}
                });

            public BadPasswordChangeResponse(string error, Dictionary<string, object> details) : base(error, details)
            {
            }
        }
    }
}