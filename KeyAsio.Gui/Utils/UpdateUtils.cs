﻿using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KeyAsio.Gui.Utils;

public class UpdateUtils
{
#nullable disable
    public class GithubRelease
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
        [JsonPropertyName("prerelease")]
        public bool PreRelease { get; set; }
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }
        [JsonPropertyName("assets")]
        public List<Asset> Assets { get; set; }
        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonIgnore]
        public string NewVerString { get; set; }
        [JsonIgnore]
        public string NowVerString { get; set; }
    }

    public class Asset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
    }

#nullable restore


}