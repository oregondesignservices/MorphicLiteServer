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

namespace Morphic.Server.Community
{

    using Db;
    using Json;

    public class Bar: Record
    {

        [JsonIgnore]
        public string CommunityId { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("is_shared")]
        public bool IsShared { get; set; } = true;

        [JsonPropertyName("items")]
        public BarItem[] Items { get; set; } = { };

        [JsonIgnore]
        public DateTime CreatedAt { get; set; }

    }

    public class BarItem: Record
    {

        [JsonPropertyName("kind")]
        [JsonRequired]
        public BarItemKind Kind { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = null!;

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("is_primary")]
        [JsonRequired]
        public bool IsPrimary { get; set; } = false;

        [JsonPropertyName("configuration")]
        public Dictionary<string, object?>? Configuration { get; set; }
    }

    public enum BarItemKind
    {
        Link,
        Application,
        Action,
    }

}