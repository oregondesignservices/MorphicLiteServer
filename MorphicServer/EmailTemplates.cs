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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Antlr3.ST;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace MorphicServer
{
    /// <summary>
    /// Class for Pending Emails.
    /// </summary>

    public abstract class EmailTemplates: BackgroundJob
    {
        // https://github.com/sendgrid/sendgrid-csharp/blob/master/USE_CASES.md#transactional-templates
        // TODO i18n? localization?
        // TODO Should the templates themselves live in the DB for easier updating (and easier localization)?

        // TODO For tracking and user inquiries, we should have an audit log.
        // Things we might need to know (i.e. things customers may call about):
        // . "I didn't request this email!" -> Source IP? Username/email? What else can we know?
        // . "I didn't get my email." -> Sendgrid logs? Do we need our own?

        /// <summary>
        /// The DB reference
        /// </summary>
        protected Database Db;

        /// <summary>
        /// The Email Settings
        /// </summary>
        protected EmailSettings EmailSettings;
        
        protected readonly ILogger logger;

        protected const string UnknownClientIp = "Unknown Client Ip";
        
        protected EmailTemplates(MorphicSettings morphicSettings, EmailSettings settings, ILogger<EmailTemplates> logger, Database db): base(morphicSettings)
        {
            Db = db;
            EmailSettings = settings;
            this.logger = logger;
        }

        protected string EmailMsgTemplate = "";
        protected string EmailSubject = "";
        protected PendingEmail.EmailTypeEnum EmailType = PendingEmail.EmailTypeEnum.None;

        protected PendingEmail CreatePendingEmail(User user, string? link, string? clientIp)
        {
            StringTemplate emailVerificationMsg = new StringTemplate(EmailMsgTemplate);
            emailVerificationMsg.SetAttribute("UserFullName", user.FullnameOrEmail());
            emailVerificationMsg.SetAttribute("UserEmail", user.GetEmail());
            if (link != null)
            {
                emailVerificationMsg.SetAttribute("Link", link);
            }
            emailVerificationMsg.SetAttribute("MorphicUser", EmailSettings.EmailFromFullname);
            emailVerificationMsg.SetAttribute("MorphicEmail", EmailSettings.EmailFromAddress);
            emailVerificationMsg.SetAttribute("ClientIp", clientIp ?? UnknownClientIp);
            var emailMsg = emailVerificationMsg.ToString();
            
            var pending = new PendingEmail(user, EmailSettings.EmailFromAddress, EmailSettings.EmailFromFullname,
                EmailSubject, 
                emailMsg,
                EmailType);
            return pending;
        }
    }

    class EmailTemplatesException : MorphicServerException
    {
        public EmailTemplatesException(string error) : base(error)
        {
        }
    }
    public class EmailVerificationEmail : EmailTemplates
    {
        public EmailVerificationEmail(MorphicSettings morphicSettings, EmailSettings settings, ILogger<EmailVerificationEmail> logger, Database db) : base(morphicSettings, settings, logger, db)
        {
            EmailMsgTemplate = @"Dear $UserFullName$,

To verify your email address $UserEmail$ please click the following link: $Link$

Regards,
$MorphicUser$ ($MorphicEmail$)";
            EmailType = PendingEmail.EmailTypeEnum.EmailValidation;
            EmailSubject = "Morphic Email Verification";
        }
        
        [AutomaticRetry(Attempts = 20)]
        public async Task QueueEmail(string userId, string? clientIp)
        {
            if (EmailSettings.Type == EmailSettings.EmailTypeDisabled)
            {
                logger.LogWarning("EmailSettings.Disable is set");
                return;
            }

            var user = await Db.Get<User>(userId);
            if (user == null)
            {
                throw new EmailTemplatesException("No User");
            }
            if (user.GetEmail() == null)
            {
                logger.LogDebug($"Sending email to user {user.Id} who doesn't have an email address");
                return;
            }

            var oneTimeToken = new OneTimeToken(user.Id);
            var verifyUri = GetFrontEndUri("/email/verify", new Dictionary<string, string>()
            {
                {"user_id", oneTimeToken.UserId},
                {"token", oneTimeToken.GetUnhashedToken()}
            });
            var pending = CreatePendingEmail(user, verifyUri.ToString(), clientIp);
            await Db.Save(oneTimeToken);
            await new SendPendingEmails(EmailSettings, logger).SendOneEmail(pending);
        }
    }

    public class PasswordResetEmail : EmailTemplates
    {
        public PasswordResetEmail(MorphicSettings morphicSettings, EmailSettings settings, ILogger<EmailVerificationEmail> logger, Database db) : base(morphicSettings, settings, logger, db)
        {
            EmailMsgTemplate =
                @"Dear $UserFullName$,

Someone requested a password reset for this email address. If this wasn't you, you may ignore
this email or contact Morphic Support.

To reset your password, please click the following link and follow the instructions: $Link$

Regards,
$MorphicUser$ ($MorphicEmail$)";
            EmailType = PendingEmail.EmailTypeEnum.PasswordResetExistingUser;
            EmailSubject = "Morphic Password Reset Requested";
        }

        [AutomaticRetry(Attempts = 20)]
        public async Task QueueEmail(string userId, string? clientIp)
        {
            if (EmailSettings.Type == EmailSettings.EmailTypeDisabled)
            {
                logger.LogWarning("EmailSettings.Disable is set");
                return;
            }

            var user = await Db.Get<User>(userId);
            if (user == null)
            {
                throw new EmailTemplatesException("No User");
            }
            if (user.GetEmail() == null)
            {
                logger.LogDebug($"Sending email to user {user.Id} who doesn't have an email address");
                return;
            }

            var oneTimeToken = new OneTimeToken(user.Id);
            var uri = GetFrontEndUri("/password/reset", new Dictionary<string, string>()
            {
                {"token", oneTimeToken.GetUnhashedToken()}
            });
            var pending = CreatePendingEmail(user, uri.ToString(), clientIp);
            await Db.Save(oneTimeToken);
            await new SendPendingEmails(EmailSettings, logger).SendOneEmail(pending);
        }
    }
    
    public class UnknownEmailPasswordResetEmail : EmailTemplates
    {
        public UnknownEmailPasswordResetEmail(MorphicSettings morphicSettings, EmailSettings settings, ILogger<EmailVerificationEmail> logger, Database db) : base(morphicSettings, settings, logger, db)
        {
            EmailMsgTemplate =
                @"Dear $UserFullName$,

Someone requested a password reset for this email address, however no account exists.
If this wasn't requested by you, you may ignore this email or contact Morphic Support.

Regards,
$MorphicUser$ ($MorphicEmail$)";
            EmailType = PendingEmail.EmailTypeEnum.PasswordResetNoUser;
            EmailSubject = "Morphic Password Reset Requested";
        }

        [AutomaticRetry(Attempts = 20)]
        public async Task QueueEmail(string destinationEmail, string? clientIp)
        {
            if (EmailSettings.Type == EmailSettings.EmailTypeDisabled)
            {
                logger.LogWarning("EmailSettings.Disable is set");
                return;
            }

            // Don't save this. It's just to carry the email
            var user = new User();
            user.SetEmail(destinationEmail);
            var pending = CreatePendingEmail(user, null, clientIp);
            await new SendPendingEmails(EmailSettings, logger).SendOneEmail(pending);
        }
    }
    
    public class EmailNotVerifiedPasswordResetEmail : EmailTemplates
    {
        public EmailNotVerifiedPasswordResetEmail(MorphicSettings morphicSettings, EmailSettings settings, ILogger<EmailVerificationEmail> logger, Database db) : base(morphicSettings, settings, logger, db)
        {
            EmailMsgTemplate =
                @"Dear $UserFullName$,

Someone requested a password reset for this email address, however the email address
has not previously been verified.
If this wasn't requested by you, you may ignore this email or contact Morphic Support.

Regards,
$MorphicUser$ ($MorphicEmail$)";
            EmailType = PendingEmail.EmailTypeEnum.PasswordResetEmailNotVerified;
            EmailSubject = "Morphic Password Reset Requested";
        }

        [AutomaticRetry(Attempts = 20)]
        public async Task QueueEmail(string destinationEmail, string? clientIp)
        {
            if (EmailSettings.Type == EmailSettings.EmailTypeDisabled)
            {
                logger.LogWarning("EmailSettings.Disable is set");
                return;
            }

            // Don't save this. It's just to carry the email
            var user = new User();
            user.SetEmail(destinationEmail);
            var pending = CreatePendingEmail(user, null, clientIp);
            await new SendPendingEmails(EmailSettings, logger).SendOneEmail(pending);
        }
    }
}