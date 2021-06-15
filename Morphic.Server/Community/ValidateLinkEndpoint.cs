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
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Web;

namespace Morphic.Server.Community
{

    using Http;
    using Billing;

    /// <summary>
    /// This allows the web client to check if a link is working.
    /// The browser is unable to perform this check itself, because cross-origin requests are forbidden
    /// in https.
    ///
    /// This does mean the server is performing network requests on behalf of the user, so there is a potential
    /// for trouble with accessing random resources.
    ///
    /// Risks:
    /// - The server is used to access something due to IP filtering.
    /// - An action on a remote server could be performed under the guise of the server.
    ///
    /// There are some limitations and mitigations:
    /// - Only authenticated users can access this.
    /// - This endpoint is accessed via a POST request.
    /// - Nothing is actually read, stored, or parsed - only the HTTP response headers.
    /// - Only normal looking URLs are accepted (http/https, non-IP, standard port)
    /// - HEAD requests are sent, then retried with a GET (some servers don't support HEAD).
    /// - The response from this endpoint is only success or failure (or validation error), nothing from
    ///    the external link is provided.
    /// - X-Forwarded-For provides the client's IP.
    /// </summary>
    [Path("/v1/validate/link/{url}")]
    public class ValidateLinkEndpoint: Endpoint
    {
        [Parameter]
        public string Url = string.Empty;

        public ValidateLinkEndpoint(IHttpContextAccessor contextAccessor, ILogger<Endpoint> logger): base(contextAccessor, logger)
        {
        }

        public override async Task LoadResource()
        {
            await RequireUser();
        }

        [Method]
        public async Task Head()
        {
            await ValidateLinkEndpoint.CheckLink(HttpUtility.UrlDecode(this.Url), this.Request.ClientIp(),
                this.Request.Headers["User-Agent"]);
        }

        /// <summary>
        /// Check if a http/https url is valid, and the resource it points to is available.
        /// </summary>
        /// <returns>Returns if the link is ok. HttpError exception if not.</returns>
        /// <exception cref="HttpError">Thrown if the link is isn't good.</exception>
        public static async Task CheckLink(string urlString, string? clientIp, string? userAgent = null, Action<Uri>? requestMaker = null)
        {
            try
            {
                if (!Uri.TryCreate(urlString, UriKind.Absolute, out Uri? parsed))
                {
                    throw new HttpError(HttpStatusCode.BadRequest);
                }

                // Only checking http
                if (parsed.IsFile || parsed.IsLoopback || parsed.IsUnc ||
                    (parsed.Scheme != "http" && parsed.Scheme != "https"))
                {
                    throw new HttpError(HttpStatusCode.BadRequest);
                }

                // Limit the scope to normal looking urls
                if (parsed.HostNameType != UriHostNameType.Dns || !parsed.IsDefaultPort ||
                    !string.IsNullOrEmpty(parsed.UserInfo))
                {
                    throw new HttpError(HttpStatusCode.BadRequest);
                }

                if (requestMaker != null)
                {
                    requestMaker(parsed);
                }
                else
                {
                    Dictionary<string, string> headers = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(clientIp))
                    {
                        headers.Add("X-Forwarded-For", clientIp);
                    }

                    if (!string.IsNullOrEmpty(userAgent))
                    {
                        headers.Add("User-Agent", userAgent);
                    }

                    HttpStatusCode headStatus = await GetLinkStatus(parsed, "HEAD", headers);
                    bool good = CheckStatusCode(headStatus);

                    if (!good && headStatus > 0)
                    {
                        // Try it again with a GET request.
                        HttpStatusCode getStatus = await GetLinkStatus(parsed, "GET", headers);
                        good = CheckStatusCode(getStatus);
                    }

                    if (!good)
                    {
                        throw new HttpError(HttpStatusCode.Gone);
                    }
                }

            }
            catch (HttpError httpError)
            {
                throw;
            }
            catch (Exception)
            {
                throw new HttpError(HttpStatusCode.Gone);
            }
        }

        /// <summary>
        /// Determines if a status code represents a valid link.
        /// </summary>
        /// <param name="statusCode">The status code</param>
        /// <returns>true if the link is good.</returns>
        private static bool CheckStatusCode(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case 0:
                    return false;
                default:
                    return (int)statusCode < 400;
            }
        }

        /// <summary>
        /// Gets the status code return by the server for the given url.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="method"></param>
        /// <param name="headers"></param>
        /// <returns>The status code, or 0 if there was a error outside the protocol or a timeout.</returns>
        private static async Task<HttpStatusCode> GetLinkStatus(Uri url, string method,
            Dictionary<string, string> headers)
        {
            HttpStatusCode statusCode;

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            try
            {
                req.Method = method;
                req.Timeout = 10000;
                req.KeepAlive = false;

                // Ignoring certificate errors. No data is being sent or received, and only checking the link
                // validity, so the user's browser will catch this error anyway. This is to stop a false
                // negative on hostnames containing an underscore, in addition to temporal problems.
                req.ServerCertificateValidationCallback += (sender, certificate, chain, errors) => true;

                foreach ((string key, string value) in headers)
                {
                    req.Headers[key] = value;
                }

                using HttpWebResponse response = (HttpWebResponse)await req.GetResponseAsync();
                statusCode = response.StatusCode;
            }
            catch (WebException webException) when (webException.Response is HttpWebResponse webResponse)
            {
                statusCode = webResponse.StatusCode;
            }
            catch (Exception)
            {
                statusCode = 0;
            }

            return statusCode;
        }


        private class LinkCheck
        {
            [JsonPropertyName("url")]
            public string Url { get; set; }
        }

        private class PlansPage
        {
            [JsonPropertyName("plans")]
            public Plan[] Plans { get; set; } = null!;
        }
    }

}