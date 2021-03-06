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
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Morphic.Server.Community{

    using Db;
    using Security;
    using Json;

    public class Member: Record
    {

        [JsonIgnore]
        public string CommunityId { get; set; } = null!;

        [JsonIgnore]
        public string? UserId { get; set; }

        [JsonPropertyName("first_name")]
        [JsonInclude]
        public EncryptedString FirstName { get; set; } = new EncryptedString();

        [JsonPropertyName("last_name")]
        [JsonInclude]
        public EncryptedString LastName { get; set; } = new EncryptedString();

        [JsonPropertyName("bar_id")]
        public string? BarId { get; set; }

        [JsonPropertyName("bar_ids")]
        public List<string> BarIds { get; set; } = new List<string>();

        [JsonPropertyName("role")]
        public MemberRole Role { get; set; } = MemberRole.Member;

        [JsonPropertyName("state")]
        public MemberState State { get; set; } = MemberState.Uninvited;

        [JsonIgnore]
        public DateTime CreatedAt { get; set; }

        [BsonIgnore]
        [JsonIgnore]
        public string? FullName
        {
            get
            {
                var firstName = FirstName.PlainText;
                var lastName = LastName.PlainText;

                if (!string.IsNullOrEmpty(firstName) && !string.IsNullOrEmpty(lastName))
                {
                    return firstName + " " + lastName;
                }
                if (!string.IsNullOrEmpty(firstName))
                {
                    return firstName;
                }
                if (!string.IsNullOrEmpty(lastName))
                {
                    return lastName;
                }
                return null;
            }
        }

    }

    public enum MemberRole
    {
        Member,
        Manager
    };

    public enum MemberState
    {
        Uninvited,
        Invited,
        Active
    }

}