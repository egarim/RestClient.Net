﻿
#if NET45
using RestClient.Net.Abstractions.Logging;
#else
using Microsoft.Extensions.Logging;
#endif

using RestClient.Net.Abstractions;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA2000

namespace RestClient.Net
{
    /// <summary>
    /// Rest client implementation using Microsoft's HttpClient class. 
    /// </summary>
    public sealed partial class Client : IClient
    {
        #region Fields
        private readonly Func<HttpClient, Func<HttpRequestMessage>, ILogger, CancellationToken, Task<HttpResponseMessage>> _sendHttpRequestFunc;
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the current IHttpClientFactory instance that is used for getting or creating HttpClient instances when the SendAsync call is made
        /// </summary>
        public IHttpClientFactory HttpClientFactory { get; }

        /// <summary>
        /// Compresses and decompresses http requests 
        /// </summary>
        public IZip Zip { get; set; }

        /// <summary>
        /// Default headers to be sent with http requests
        /// </summary>
        public IHeadersCollection DefaultRequestHeaders { get; }

        /// <summary>
        /// Default timeout for http requests
        /// </summary>
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Adapter for serialization/deserialization of http body data
        /// </summary>
        public ISerializationAdapter SerializationAdapter { get; }

        /// <summary>
        /// Logging abstraction that will trace request/response data and log events
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Specifies whether or not the client will throw an exception when non-successful status codes are returned in the http response. The default is true
        /// </summary>
        public bool ThrowExceptionOnFailure { get; set; } = true;

        /// <summary>
        /// Base Uri for the client. Any resources specified on requests will be relative to this.
        /// </summary>
        public Uri BaseUri { get; set; }

        /// <summary>
        /// Name of the client
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the current IRequestConverter instance responsible for converting rest requests to http requests
        /// </summary>
        public IRequestConverter RequestConverter { get; }
        #endregion

        #region Func
        private static readonly Func<HttpClient, Func<HttpRequestMessage>, ILogger, CancellationToken, Task<HttpResponseMessage>> DefaultSendHttpRequestMessageFunc = async (httpClient, httpRequestMessageFunc, logger, cancellationToken) =>
        {
            try
            {
                var httpRequestMessage = httpRequestMessageFunc.Invoke();

                logger?.LogTrace(new Trace(HttpRequestMethod.Custom, TraceEvent.Information, message: $"Attempting to send with the HttpClient. HttpClient Null: {httpClient == null}"));

                var httpResponseMessage = await httpClient.SendAsync(httpRequestMessage, cancellationToken);

                logger?.LogTrace(new Trace(HttpRequestMethod.Custom, TraceEvent.Information, message: $"SendAsync on HttpClient returned without an exception"));

                return httpResponseMessage;
            }
            catch (Exception ex)
            {
                logger?.LogException(new Trace(
                HttpRequestMethod.Custom,
                TraceEvent.Error,
                null,
                message: $"Exception: {ex}"),
                ex);

                throw;
            }
        };
        #endregion

        #region Constructors
        /// <summary>
        /// Construct a client
        /// </summary>
        /// <param name="serializationAdapter">The serialization adapter for serializing/deserializing http content bodies</param>
        public Client(
            ISerializationAdapter serializationAdapter)
        : this(
            serializationAdapter,
            default)
        {
        }

        /// <summary>
        /// Construct a client
        /// </summary>
        /// <param name="serializationAdapter">The serialization adapter for serializing/deserializing http content bodies</param>
        /// <param name="baseUri">The base Url for the client. Specify this if the client will be used for one Url only</param>
        public Client(
            ISerializationAdapter serializationAdapter,
            Uri baseUri)
        : this(
            serializationAdapter,
            null,
            baseUri)
        {
        }

        /// <summary>
        /// Construct a client.
        /// </summary>
        /// <param name="serializationAdapter">The serialization adapter for serializing/deserializing http content bodies</param>
        /// <param name="logger"></param>
        /// <param name="httpClientFactory"></param>
        public Client(
            ISerializationAdapter serializationAdapter,
            ILogger logger,
            IHttpClientFactory httpClientFactory)
        : this(
            serializationAdapter,
            null,
            null,
            logger: logger,
            httpClientFactory: httpClientFactory)
        {
        }
        #endregion

        #region Implementation
        async Task<Response<TResponseBody>> IClient.SendAsync<TResponseBody, TRequestBody>(Request<TRequestBody> request)
        {
            HttpResponseMessage httpResponseMessage;
            byte[] requestBodyData = null;
            HttpClient httpClient = null;

            try
            {
                Logger?.LogTrace(new Trace(request.HttpRequestMethod, TraceEvent.Information, request.Resource, message: $"Begin send"));

                httpClient = HttpClientFactory.CreateClient(Name);

                Logger?.LogTrace(new Trace(request.HttpRequestMethod, TraceEvent.Information, request.Resource, message: $"Got HttpClient null: {httpClient == null}"));

                //Note: if HttpClient naming is not handled properly, this may alter the HttpClient of another RestClient
                if (httpClient.Timeout != Timeout && Timeout != default) httpClient.Timeout = Timeout;
                if (httpClient.BaseAddress != BaseUri && BaseUri != null) httpClient.BaseAddress = BaseUri;

                Logger?.LogTrace(new Trace(request.HttpRequestMethod, TraceEvent.Information, request.Resource, message: $"HttpClient configured. Request Null: {request == null} Adapter Null: {SerializationAdapter == null}"));

                requestBodyData = null;

                if (DefaultRequestConverter.UpdateHttpRequestMethods.Contains(request.HttpRequestMethod))
                {
                    requestBodyData = SerializationAdapter.Serialize(request.Body, request.Headers);
                    Logger?.LogTrace(new Trace(request.HttpRequestMethod, TraceEvent.Information, request.Resource, requestBodyData, message: $"Request body serialized"));
                }
                else
                {
                    Logger?.LogTrace(new Trace(request.HttpRequestMethod, TraceEvent.Information, request.Resource, requestBodyData, message: $"No request body to serialize"));
                }

                httpResponseMessage = await _sendHttpRequestFunc.Invoke(
                    httpClient,
                    () => RequestConverter.GetHttpRequestMessage(request, requestBodyData),
                    Logger,
                    request.CancellationToken
                    );
            }
            catch (TaskCanceledException tce)
            {
                Logger?.LogException(new Trace(
                    request.HttpRequestMethod,
                    TraceEvent.Error,
                    request.Resource,
                    message: $"Exception: {tce}"),
                    tce);

                throw;
            }
            catch (OperationCanceledException oce)
            {
                Logger?.LogException(new Trace(
                    request.HttpRequestMethod,
                    TraceEvent.Error,
                    request.Resource,
                    message: $"Exception: {oce}"),
                    oce);

                throw;
            }
            //TODO: Does this need to be handled like this?
            catch (Exception ex)
            {
                var exception = new SendException<TRequestBody>("HttpClient Send Exception", request, ex);

                Logger?.LogException(new Trace(
                request.HttpRequestMethod,
                TraceEvent.Error,
                request.Resource,
                message: $"Exception: {ex}"),
                exception);

                throw exception;
            }

            Logger?.LogTrace(new Trace
                (
                    request.HttpRequestMethod,
                    TraceEvent.Request,
                    httpResponseMessage.RequestMessage?.RequestUri,
                    requestBodyData,
                    null,
                    request.Headers,
                    "Request was sent to server"
                ));

            return await ProcessResponseAsync<TResponseBody, TRequestBody>(request, httpResponseMessage, httpClient);
        }

        private async Task<Response<TResponseBody>> ProcessResponseAsync<TResponseBody, TRequestBody>(Request<TRequestBody> request, HttpResponseMessage httpResponseMessage, HttpClient httpClient)
        {
            byte[] responseData = null;

            if (Zip != null)
            {
                //This is for cases where an unzipping utility needs to be used to unzip the content. This is actually a bug in UWP
                var gzipHeader = httpResponseMessage.Content.Headers.ContentEncoding.FirstOrDefault(h =>
                    !string.IsNullOrEmpty(h) && h.Equals("gzip", StringComparison.OrdinalIgnoreCase));
                if (gzipHeader != null)
                {
                    var bytes = await httpResponseMessage.Content.ReadAsByteArrayAsync();
                    responseData = Zip.Unzip(bytes);
                }
            }

            if (responseData == null)
            {
                responseData = await httpResponseMessage.Content.ReadAsByteArrayAsync();
            }

            var httpResponseHeadersCollection = new HttpResponseHeadersCollection(httpResponseMessage.Headers);

            TResponseBody responseBody;
            try
            {
                responseBody = SerializationAdapter.Deserialize<TResponseBody>(responseData, httpResponseHeadersCollection);
            }
            catch (Exception ex)
            {
                throw new DeserializationException(Messages.ErrorMessageDeserialization, responseData, this, ex);
            }

            var httpResponseMessageResponse = new HttpResponseMessageResponse<TResponseBody>
            (
                httpResponseHeadersCollection,
                (int)httpResponseMessage.StatusCode,
                request.HttpRequestMethod,
                responseData,
                responseBody,
                httpResponseMessage,
                httpClient
            );

            Logger?.LogTrace(new Trace
            (
             request.HttpRequestMethod,
                TraceEvent.Response,
                httpResponseMessage.RequestMessage?.RequestUri,
                responseData,
                (int)httpResponseMessage.StatusCode,
                httpResponseHeadersCollection
            ));

            if (httpResponseMessageResponse.IsSuccess || !ThrowExceptionOnFailure)
            {
                return httpResponseMessageResponse;
            }

            throw new HttpStatusException($"Non successful Http Status Code: {httpResponseMessageResponse.StatusCode}.\r\nRequest Uri: {httpResponseMessage.RequestMessage.RequestUri}", httpResponseMessageResponse, this);
        }
        #endregion
    }
}

#pragma warning restore CA2000