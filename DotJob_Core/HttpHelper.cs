﻿using System.Net.Http.Headers;
using System.Text.Json;


namespace Host
{
    /// <summary>
    /// 请求帮助类
    /// </summary>
    public class HttpHelper
    {
        public static readonly HttpHelper Instance;

        /// <summary>
        /// 共享的 SocketsHttpHandler — 管理底层连接池，所有 HttpClient 共用
        /// </summary>
        private static readonly SocketsHttpHandler SharedHandler;

        /// <summary>
        /// 共享的无 Header 默认 HttpClient（线程安全，可复用）
        /// </summary>
        private static readonly HttpClient SharedClient;

        static HttpHelper()
        {
            // 统一底层连接池配置，避免 Socket 耗尽
            SharedHandler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer    = 100,            // 每个目标主机最多 100 并发连接
                PooledConnectionLifetime   = TimeSpan.FromMinutes(5),  // 连接存活 5 分钟后回收
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2), // 空闲 2 分钟回收
                ConnectTimeout             = TimeSpan.FromSeconds(10), // 建立连接超时 10 秒
            };

            SharedClient = new HttpClient(SharedHandler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30) // HTTP 请求整体超时 30 秒
            };

            Instance = new HttpHelper();
        }

        /// <summary>
        /// 创建带自定义 Header 的 HttpClient（共享同一个连接池）
        /// </summary>
        private static HttpClient CreateClientWithHeaders(Dictionary<string, string> headers)
        {
            var client = new HttpClient(SharedHandler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            foreach (var item in headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(item.Key, item.Value);
            }
            return client;
        }

        /// <summary>
        /// Post请求
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string url, string jsonString, Dictionary<string, string>? headers = null)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                jsonString = "{}";
            var content = new StringContent(jsonString);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (headers != null && headers.Any())
            {
                using var http = CreateClientWithHeaders(headers);
                return await http.PostAsync(new Uri(url), content);
            }
            return await SharedClient.PostAsync(new Uri(url), content);
        }

        /// <summary>
        /// Post请求
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync<T>(string url, T content, Dictionary<string, string>? headers = null) where T : class
        {
            return await PostAsync(url, JsonSerializer.Serialize(content), headers);
        }

        /// <summary>
        /// Get请求
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string url, Dictionary<string, string>? headers = null)
        {
            if (headers != null && headers.Any())
            {
                using var http = CreateClientWithHeaders(headers);
                return await http.GetAsync(url);
            }
            return await SharedClient.GetAsync(url);
        }

        /// <summary>
        /// Put请求
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string url, string jsonString, Dictionary<string, string>? headers = null)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
                jsonString = "{}";
            var content = new StringContent(jsonString);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (headers != null && headers.Any())
            {
                using var http = CreateClientWithHeaders(headers);
                return await http.PutAsync(url, content);
            }
            return await SharedClient.PutAsync(url, content);
        }

        /// <summary>
        /// Put请求
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync<T>(string url, T content, Dictionary<string, string>? headers = null)
        {
            return await PutAsync(url, JsonSerializer.Serialize(content), headers);
        }

        /// <summary>
        /// Delete请求
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string url, Dictionary<string, string>? headers = null)
        {
            if (headers != null && headers.Any())
            {
                using var http = CreateClientWithHeaders(headers);
                return await http.DeleteAsync(url);
            }
            return await SharedClient.DeleteAsync(url);
        }
    }
}

