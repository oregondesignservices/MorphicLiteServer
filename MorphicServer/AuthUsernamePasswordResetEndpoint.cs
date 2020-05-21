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
using MorphicServer.Attributes;
using Serilog;
using Serilog.Context;

namespace MorphicServer
{
    /// <summary>
    /// API To reset a user's username-auth credential password. hence: AuthUsernamePasswordResetEndpoint
    /// (trust me, it gets worse in the other endpoint).
    ///
    /// This will reset the password to the given password, if the one-time-token is valid.
    ///
    /// TODO: This is an API and not a web-page. We probably should have a web-page for this.
    /// </summary>
    [Path("/v1/auth/username/password_reset/{oneTimeToken}")]
    public class AuthUsernamePasswordResetEndpoint : Endpoint
    {
        /// <summary>The lookup id to use, populated from the request URL</summary>
        [Parameter] public string oneTimeToken = "";

        /// <summary>The UsernameCredential data populated by <code>LoadResource()</code></summary>
        private UsernameCredential usernameCredentials = null!;

        /// <summary>The limited-use token data populated by <code>LoadResource()</code></summary>
        public OneTimeToken OneTimeToken = null!;

        public override async Task LoadResource()
        {
            var hashedToken = OneTimeToken.TokenHashedWithDefault(oneTimeToken);
            try
            {
                OneTimeToken = await Load<OneTimeToken>(hashedToken);
                if (OneTimeToken == null || !OneTimeToken.IsValid())
                {
                    throw new HttpError(HttpStatusCode.NotFound, BadPasswordResetResponse.InvalidToken);
                }
            }
            catch (HttpError httpError)
            {
                throw new HttpError(httpError.Status, BadPasswordResetResponse.InvalidToken);
            }

            try
            {
                usernameCredentials = await Load<UsernameCredential>(u => u.UserId == OneTimeToken.UserId);
            }
            catch (HttpError httpError)
            {
                throw new HttpError(httpError.Status, BadPasswordResetResponse.UserNotFound);
            }
        }

        /// <summary>Fetch the user</summary>
        [Method]
        public async Task Post()
        {
            var request = await Request.ReadJson<PasswordResetRequest>();
            usernameCredentials.SetPassword(request.NewPassword);
            await Save(usernameCredentials);
            await OneTimeToken.Invalidate(Context.GetDatabase());
            if (request.DeleteExistingTokens)
            {
                await Delete<AuthToken>(token => token.UserId == usernameCredentials.UserId);
            }

            // TODO Need to respond with a nicer webpage than this
            await Respond(new SuccessResponse("password_was_reset"));
        }

        public class PasswordResetRequest
        {
            [JsonPropertyName("new_password")] public string NewPassword { get; set; } = null!;

            [JsonPropertyName("delete_existing_tokens")]
            public bool DeleteExistingTokens { get; set; } = false;
        }

        public class SuccessResponse
        {
            [JsonPropertyName("message")] public string Status { get; }

            public SuccessResponse(string message)
            {
                Status = message;
            }
        }

        public class BadPasswordResetResponse : BadRequestResponse
        {
            public static readonly BadPasswordResetResponse
                InvalidToken = new BadPasswordResetResponse("invalid_token");

            public static readonly BadPasswordResetResponse UserNotFound = new BadPasswordResetResponse("invalid_user");

            public BadPasswordResetResponse(string error) : base(error)
            {
            }

        }
    }

    /// <summary>
    /// The API to request a password-reset link for username auth. Therefore AuthUsernamePasswordResetRequestEndpoint.
    ///
    /// This will send an email to the email in the request json, whether that email exists or not.
    ///
    /// TODO Rate limit the emails we send to a given email, especially this stuff.
    /// </summary>
    [Path("/v1/auth/username/password_reset/request")]
    public class AuthUsernamePasswordResetRequestEndpoint : Endpoint
    {
        /// <summary>
        /// TODO: Need to rate-limit this and/or use re-captcha
        /// </summary>
        /// <returns></returns>
        [Method]
        public async Task Post()
        {
            var request = await Request.ReadJson<PasswordResetRequestRequest>();
            if (request.Email == "")
            {
                throw new HttpError(HttpStatusCode.BadRequest, BadPasswordRequestResponse.MissingRequired);
            }
            var db = Context.GetDatabase();
            var hash = User.UserEmailHashCombined(request.Email);
            using (LogContext.PushProperty("EmailHash", hash))
            {
                var user = await db.Get<User>(a => a.EmailHash == hash, ActiveSession);
                if (user != null)
                {
                    Log.Logger.Information("Password reset requested for userId {userId}", user.Id);
                    BackgroundJob.Enqueue<NewPasswordResetEmail>(x => x.QueueEmail(user.Id,
                        GetControllerPathUrl<AuthUsernamePasswordResetEndpoint>(Request.Headers,
                            Context.GetMorphicSettings()),
                            ClientIpFromRequest(Request)));
                }
                else
                {
                    Log.Logger.Information("Password reset requested but no email matching");
                    BackgroundJob.Enqueue<NewNoEmailPasswordResetEmail>(x => x.QueueEmail(
                        request.Email,
                        ClientIpFromRequest(Request)));
                }
            }
        }

        /// <summary>
        /// Model the password-reset-request request (yea I know...)
        /// </summary>
        public class PasswordResetRequestRequest
        {
            [JsonPropertyName("email")]
            public string Email { get; set; } = null!;
        }
        
        public class BadPasswordRequestResponse : BadRequestResponse
        {
            public static readonly BadPasswordRequestResponse MissingRequired = new BadPasswordRequestResponse(
                "missing_required",
                new Dictionary<string, object>
                {
                    {"required", new List<string> { "email" } }
                });
            public BadPasswordRequestResponse(string error, Dictionary<string, object> details) : base(error, details)
            {
            }
        }

    }
}