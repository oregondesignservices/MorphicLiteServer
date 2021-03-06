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
    using Users;

    [Path("/v1/users/{userid}/resend_verification")]
    public class ResendEmailVerificationEndpoint : Endpoint
    {
        private IBackgroundJobClient jobClient;
        
        public ResendEmailVerificationEndpoint(
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

            user = authenticatedUser;
        }

        private User user = null!;

        [Method]
        public Task Post()
        {
            if (!user.EmailVerified)
            {
                jobClient.Enqueue<EmailVerificationEmail>(x => x.SendEmail(
                    user.Id,
                    Request.ClientIp()
                ));
            }
            return Task.CompletedTask;
        }

    }
}