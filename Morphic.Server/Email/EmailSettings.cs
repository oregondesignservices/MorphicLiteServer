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

namespace Morphic.Server.Email
{
    public class EmailSettings
    {
        public static readonly string EmailTypeDisabled = "disabled";
        public static readonly string EmailTypeSendgrid = "sendgrid";
        public static readonly string EmailTypeSendInBlue = "sendinblue";
        public static readonly string EmailTypeLog = "log";
        
        /// <summary>
        /// The type of email sending we want. Supported: "sendgrid", "log", "disabled"
        /// </summary>
        public string Type { get; set; } = EmailTypeDisabled;
        
        public string EmailFromAddress { get; set; } = "support@morphic.org";
        /// <summary>
        /// The default 'name' we use to send emails.
        /// </summary>
        public string EmailFromFullname { get; set; } = "Morphic Support";

        public string? InternalAlertEmail { get; set; }

        /// <summary>
        /// Some SendGrid settings.
        /// </summary>
        public SendGridSettings SendGridSettings { get; set; } = null!;

        public SendInBlueSettings SendInBlueSettings { get; set; } = null!;
    }
}