using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Morphic.Server.Email
{
    public class SendGridSettings
    {
        /// <summary>
        /// The Sendgrid API key.
        /// 
        /// NOTE: Do not put this into any appsettings file. It's a secret and should be
        /// configured via environment variables
        ///
        /// For production: Put it in repo: deploy-morphiclite, path: environments/*/secrets/all.env
        /// depending on the environment
        ///
        /// For development and others, see launchSettings.json or the docker-compose.morphicserver.yml file.
        /// </summary>
        public string ApiKey { get; set; } = "";

        public string WelcomeEmailValidationId { get; set; } = "d-1d54234ca083487c8a14e3fba27c9e6a";
        public string PasswordResetId { get; set; } = "d-8dbb4d6c44cb423f8bef356b832246bd";
        public string PasswordResetEmailNotValidatedId { get; set; } = "d-aecd70a619eb49deadbb74451797dd04";
        public string PasswordResetUnknownEmailId { get; set; } = "d-ea0b8cbcb7524f58a385b7dc02cde30a";
        public string ChangePasswordEmailId { get; set; } = "d-60a9ceb7fe084988939dc8f114095a33";
    }

    public class Sendgrid : SendEmailWorker
    {
        public Sendgrid(EmailSettings emailSettings, ILogger logger) : base(emailSettings, logger)
        {
            if (emailSettings.Type == EmailSettings.EmailTypeSendgrid &&
                (emailSettings.SendGridSettings == null || emailSettings.SendGridSettings.ApiKey == ""))
            {
                throw new SendgridException("misconfigured settings");
            }
        }

        private string EmailTypeToSendgridId(EmailConstants.EmailTypes emailType)
        {
            string emailTemplateId = "";
            switch (emailType)
            {
                case EmailConstants.EmailTypes.PasswordReset:
                    emailTemplateId = emailSettings.SendGridSettings.PasswordResetId;
                    break;
                case EmailConstants.EmailTypes.WelcomeEmailValidation:
                    emailTemplateId = emailSettings.SendGridSettings.WelcomeEmailValidationId;
                    break;
                case EmailConstants.EmailTypes.PasswordResetUnknownEmail:
                    emailTemplateId = emailSettings.SendGridSettings.PasswordResetUnknownEmailId;
                    break;
                case EmailConstants.EmailTypes.PasswordResetEmailNotValidated:
                    emailTemplateId = emailSettings.SendGridSettings.PasswordResetEmailNotValidatedId;
                    break;
                case EmailConstants.EmailTypes.ChangePasswordEmail:
                    emailTemplateId = emailSettings.SendGridSettings.ChangePasswordEmailId;
                    break;
                case EmailConstants.EmailTypes.None:
                    throw new SendgridException("EmailType None");
            }

            if (emailTemplateId == "")
            {
                throw new SendgridException("EmailType Unknown: " + emailType.ToString());
            }

            return emailTemplateId;
        }

        public override async Task<bool> SendTemplate(EmailConstants.EmailTypes emailType, Dictionary<string, string> emailAttributes)
        {
            var emailTemplateId = EmailTypeToSendgridId(emailType);
            var from = new EmailAddress(emailAttributes["FromEmail"], emailAttributes["FromUserName"]);
            var to = new EmailAddress(emailAttributes["ToEmail"], emailAttributes["ToUserName"]);
            var msg = MailHelper.CreateSingleTemplateEmail(from, to, emailTemplateId, emailAttributes);
            return await SendViaSendGrid(msg);
        }

        private async Task<bool> SendViaSendGrid(SendGridMessage msg)
        {
            var client = new SendGridClient(emailSettings.SendGridSettings.ApiKey);
            var response = await client.SendEmailAsync(msg);

            if (response.StatusCode < HttpStatusCode.OK || response.StatusCode >= HttpStatusCode.Ambiguous)
            {
                logger.LogError("Email send failed: {StatusCode} {Headers} {Body}", response.StatusCode,
                    response.Headers, response.Body.ReadAsStringAsync().Result);
                return false;
            }
            else
            {
                logger.LogDebug("Email send succeeded: {StatusCode} {Headers} {Body}", response.StatusCode,
                    response.Headers, response.Body.ReadAsStringAsync().Result);
                return true;
            }
        }
        
        class SendgridException : MorphicServerException
        {
            public SendgridException(string error) : base(error)
            {
            }
        }
    }
}