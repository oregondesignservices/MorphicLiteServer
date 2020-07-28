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

namespace Morphic.Server.Payments
{
    using Db;
    using Users;
    
    public enum PaymentProcessors
    {
        None = 0,
        Stripe = 1,
    }

    /// <summary>
    /// The _id of this record is to be considered the 'morphic transactionId'
    /// </summary>
    public class PaymentTransaction : Record
    {
        public PaymentTransaction(
            User user,
            PaymentProcessors processor,
            string transactionKey,
            long amount,
            string currency)
        {
            Id = Guid.NewGuid().ToString();
            UserId = user.Id;
            Processor = processor;
            TransactionKey = transactionKey;
            Amount = amount;
            Currency = currency;
        }

        /// <summary>
        /// Link to the userId
        /// </summary>
        public string UserId { get; set;  }
        
        public string TransactionKey { get; set; }
        
        /// <summary>
        /// The payment processor used for this customer
        /// </summary>
        public PaymentProcessors Processor { get; set; }

        public CreditCardInfo? CreditCardInfo { get; set; }
        public string? CreditCardId { get; set; }
        public long Amount;
        public string Currency;
    }
}