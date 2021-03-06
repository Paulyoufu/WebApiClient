﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebApiClient.Interfaces;

namespace WebApiClient.Defaults
{
    /// <summary>
    /// 表示默认的HttpClient
    /// </summary>
    public class HttpClient : IHttpClient
    {
        /// <summary>
        /// HttpClient实例
        /// </summary>
        private System.Net.Http.HttpClient client;

        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool isDisposed;

        /// <summary>
        /// 正在挂起的请求
        /// </summary>
        private long pendingCount = 0L;

        /// <summary>
        /// 是否支持创建Handler
        /// </summary>
        private readonly bool supportCreateHandler = false;

        /// <summary>
        /// 获取关联的Http处理对象
        /// </summary>
        public HttpClientHandler Handler { get; private set; }

        /// <summary>
        /// 获取默认的请求头管理对象
        /// </summary>
        public HttpRequestHeaders DefaultRequestHeaders
        {
            get
            {
                return this.client.DefaultRequestHeaders;
            }
        }

        /// <summary>
        /// 获取或设置请求超时时间
        /// </summary>
        public TimeSpan Timeout
        {
            get
            {
                return this.client.Timeout;
            }
            set
            {
                this.client.Timeout = value;
            }
        }

        /// <summary>
        /// 获取或设置最大回复内容长度
        /// </summary>
        public long MaxResponseContentBufferSize
        {
            get
            {
                return this.client.MaxResponseContentBufferSize;
            }
            set
            {
                this.client.MaxResponseContentBufferSize = value;
            }
        }

        /// <summary>
        /// 默认的HttpClient
        /// </summary>
        public HttpClient() :
            this(handler: null, disposeHandler: true, supportCreateHandler: true)
        {
        }

        /// <summary>
        /// 默认的HttpClient
        /// </summary>
        /// <param name="handler">关联的Http处理对象</param>
        /// <param name="disposeHandler">调用Dispose方法时，是否也Dispose handler</param>
        /// <exception cref="ArgumentNullException"></exception>
        public HttpClient(HttpClientHandler handler, bool disposeHandler = false)
            : this(handler ?? throw new ArgumentNullException(nameof(handler)), disposeHandler, false)
        {
        }

        /// <summary>
        /// 默认的HttpClient
        /// </summary>
        /// <param name="handler"></param>   
        /// <param name="disposeHandler">调用HttpClient.Dispose时是否也disposeHandler</param>
        /// <param name="supportCreateHandler">是否支持调用创建实例</param>
        private HttpClient(HttpClientHandler handler, bool disposeHandler, bool supportCreateHandler)
        {
            this.supportCreateHandler = supportCreateHandler;
            this.Handler = handler ?? this.CreateHttpClientHandler();
            this.client = new System.Net.Http.HttpClient(this.Handler, disposeHandler);
        }

        /// <summary>
        /// 设置Cookie值到Cookie容器
        /// 当Handler.UseCookies才添加
        /// </summary>
        /// <param name="domain">cookie域名</param>
        /// <param name="cookieValues">cookie值，可以不编码，eg：key1=value1; key2=value2</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public bool SetCookie(Uri domain, string cookieValues)
        {
            if (this.Handler.UseCookies == false)
            {
                return false;
            }

            if (domain == null)
            {
                throw new ArgumentNullException(nameof(domain));
            }

            foreach (var cookie in EncodeCookies(cookieValues, Encoding.UTF8))
            {
                this.Handler.CookieContainer.Add(domain, cookie);
            }
            return true;
        }

        /// <summary>
        /// 给cookie编码
        /// </summary>
        /// <param name="cookieValues">cookie文本</param>
        /// <param name="encoding">编码</param>
        /// <returns></returns>
        private static IEnumerable<Cookie> EncodeCookies(string cookieValues, Encoding encoding)
        {
            if (cookieValues == null)
            {
                return Enumerable.Empty<Cookie>();
            }

            return from item in cookieValues.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                   let kv = item.Split('=')
                   let name = kv.FirstOrDefault().Trim()
                   let value = kv.Length > 1 ? kv.LastOrDefault() : string.Empty
                   let encode = HttpUtility.UrlEncode(value, encoding)
                   select new Cookie(name, encode);
        }

        /// <summary>
        /// 设置代理
        /// </summary>
        /// <param name="proxy">代理，为null则清除代理</param>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <returns></returns>
        public bool SetProxy(IWebProxy proxy)
        {
            if (this.isDisposed == true)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }

            if (Interlocked.Read(ref this.pendingCount) > 0L)
            {
                throw new InvalidOperationException("当前还有未完成的请求，不能更换代理");
            }

            if (this.Handler.SupportsProxy == false)
            {
                return false;
            }

            if (IsProxyEquals(this.Handler.Proxy, proxy) == true)
            {
                return false;
            }

            // 设置代理前释放实例并重新初始化
            if (this.Handler.Proxy != null)
            {
                this.InitWithoutProxy();
            }

            this.Handler.UseProxy = proxy != null;
            this.Handler.Proxy = proxy;
            return true;
        }

        /// <summary>
        /// 重新初始化HttpClient和Handler实例
        /// </summary>
        private void InitWithoutProxy()
        {
            var handler = this.CreateHttpClientHandler();
            CopyProperties(this.Handler, handler);
            handler.UseProxy = false;
            handler.Proxy = null;

            var httpClient = new System.Net.Http.HttpClient(handler);
            CopyProperties(this.client, httpClient);
            this.client.Dispose();

            this.client = httpClient;
            this.Handler = handler;
        }

        /// <summary>
        /// 创建HttpClientHandler的新实例
        /// </summary>
        /// <returns></returns>
        protected virtual HttpClientHandler CreateHttpClientHandler()
        {
            if (this.supportCreateHandler == false)
            {
                throw new NotSupportedException("不支持创建新的HttpClientHandler实例");
            }
            return new DefaultHttpClientHandler();
        }

        /// <summary>
        /// 复制source的属性到target
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private static bool CopyProperties<T>(T source, T target)
        {
            var state = true;
            var properties = source.GetType()
                .GetProperties()
                .Where(item => item.CanRead && item.CanWrite);

            foreach (var propery in properties)
            {
                try
                {
                    var value = propery.GetValue(source);
                    propery.SetValue(target, value);
                }
                catch (Exception)
                {
                    state = false;
                }
            }
            return state;
        }

        /// <summary>
        /// 目录网址
        /// </summary>
        private static readonly Uri destination = new Uri("http://www.webapiclient.com");

        /// <summary>
        /// 比较代理是否相等
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        private static bool IsProxyEquals(IWebProxy x, IWebProxy y)
        {
            if (x == null && y == null)
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            return x.GetProxy(destination) == y.GetProxy(destination);
        }

        /// <summary>
        /// 异步发送请求
        /// </summary>
        /// <param name="request">请求消息</param>
        /// <returns></returns>
        public async Task<HttpResponseMessage> SendAsync(HttpApiRequestMessage request)
        {
            if (request.RequestUri == null)
            {
                throw new HttpApiConfigException("未配置RequestUri，RequestUri不能为null");
            }

            try
            {
                Interlocked.Increment(ref this.pendingCount);

                var timeout = request.Timeout ?? this.Timeout;
                var cancellationToken = new CancellationTokenSource(timeout).Token;
                return await this.client.SendAsync(request, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref this.pendingCount);
            }
        }

        /// <summary>
        /// 取消挂起的请求
        /// </summary>
        public void CancelPendingRequests()
        {
            this.client.CancelPendingRequests();
        }

        /// <summary>
        /// 释放httpClient
        /// </summary>
        public void Dispose()
        {
            this.client.Dispose();
            this.isDisposed = true;
        }
    }
}
