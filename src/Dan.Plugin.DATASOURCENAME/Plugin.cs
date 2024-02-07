using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Exceptions;
using Dan.Common.Interfaces;
using Dan.Common.Models;
using Dan.Common.Util;
using Dan.Plugin.DATASOURCENAME.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Dan.Plugin.DATASOURCENAME;

public class Plugin
{
    private readonly IEvidenceSourceMetadata _evidenceSourceMetadata;
    private readonly ILogger _logger;
    private readonly HttpClient _client;
    private readonly Settings _settings;

    // The datasets must supply a human-readable source description from which they originate. Individual fields might come from different sources, and this string should reflect that (ie. name all possible sources).
    public const string SourceName = "Digitaliseringsdirektoratet";

    // The function names (ie. HTTP endpoint names) and the dataset names must match. Using constants to avoid errors.
    public const string SimpleDatasetName = "SimpleDataset";
    public const string RichDatasetName = "RichDataset";

    // These are not mandatory, but there should be a distinct error code (any integer) for all types of errors that can occur. The error codes does not have to be globally
    // unique. These should be used within either transient or permanent exceptions, see Plugin.cs for examples.
    private const int ERROR_UPSTREAM_UNAVAILBLE = 1001;
    private const int ERROR_INVALID_INPUT = 1002;
    private const int ERROR_NOT_FOUND = 1003;
    private const int ERROR_UNABLE_TO_PARSE_RESPONSE = 1004;

    public Plugin(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        IOptions<Settings> settings,
        IEvidenceSourceMetadata evidenceSourceMetadata)
    {
        _client = httpClientFactory.CreateClient(Constants.SafeHttpClient);
        _logger = loggerFactory.CreateLogger<Plugin>();
        _settings = settings.Value;
        _evidenceSourceMetadata = evidenceSourceMetadata;

        _logger.LogDebug("Initialized plugin! This should be visible in the console");
    }

    [Function(SimpleDatasetName)]
    public async Task<HttpResponseData> GetSimpleDatasetAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
        FunctionContext context)
    {

        _logger.LogDebug("debug HERE");
        _logger.LogWarning("warning HERE");
        _logger.LogError("error HERE");

        var evidenceHarvesterRequest = await req.ReadFromJsonAsync<EvidenceHarvesterRequest>();

        return await EvidenceSourceResponse.CreateResponse(req,
            () => GetEvidenceValuesSimpledataset(evidenceHarvesterRequest));
    }

    [Function(RichDatasetName)]
    public async Task<HttpResponseData> GetRichDatasetAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestData req,
        FunctionContext context)
    {
        var evidenceHarvesterRequest = await req.ReadFromJsonAsync<EvidenceHarvesterRequest>();

        return await EvidenceSourceResponse.CreateResponse(req,
            () => GetEvidenceValuesRichDataset(evidenceHarvesterRequest));
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesSimpledataset(EvidenceHarvesterRequest evidenceHarvesterRequest)
    {
        var url = _settings.EndpointUrl + "?someparameter=" + evidenceHarvesterRequest.OrganizationNumber;
        var exampleModel = await MakeRequest<ExampleModel>(url);

        var ecb = new EvidenceBuilder(_evidenceSourceMetadata, SimpleDatasetName);
        ecb.AddEvidenceValue("field1", exampleModel.ResponseField1, SourceName);
        ecb.AddEvidenceValue("field2", exampleModel.ResponseField2, SourceName);

        return ecb.GetEvidenceValues();
    }

    private async Task<List<EvidenceValue>> GetEvidenceValuesRichDataset(EvidenceHarvesterRequest evidenceHarvesterRequest)
    {

        var url = _settings.EndpointUrl + "?someparameter=" + evidenceHarvesterRequest.OrganizationNumber;
        var exampleModel = await MakeRequest<ExampleModel>(url);

        var ecb = new EvidenceBuilder(_evidenceSourceMetadata, RichDatasetName);

        // Here we reserialize the model. While it is possible to merely send the received JSON string directly through without parsing it,
        // the extra step of deserializing it to a known model ensures that the JSON schema supplied in the metadata always matches the
        // dataset model.
        //
        // Another way to do this is to not generate the schema from the model, but "hand code" the schema in the metadata and validate the
        // received JSON against it, throwing eg. a EvidenceSourcePermanentServerException if it fails to match.
        ecb.AddEvidenceValue("default", JsonConvert.SerializeObject(exampleModel), SourceName);

        return ecb.GetEvidenceValues();
    }

    private async Task<T> MakeRequest<T>(string target)
    {
        HttpResponseMessage result;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, target);
            result = await _client.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new EvidenceSourceTransientException(ERROR_UPSTREAM_UNAVAILBLE, "Error communicating with upstream source", ex);
        }

        if (!result.IsSuccessStatusCode)
        {
            throw result.StatusCode switch
            {
                HttpStatusCode.NotFound => new EvidenceSourcePermanentClientException(ERROR_NOT_FOUND, "Upstream source could not find the requested entity (404)"),
                HttpStatusCode.BadRequest => new EvidenceSourcePermanentClientException(ERROR_INVALID_INPUT,  "Upstream source indicated an invalid request (400)"),
                _ => new EvidenceSourceTransientException(ERROR_UPSTREAM_UNAVAILBLE, $"Upstream source retuned an HTTP error code ({(int)result.StatusCode})")
            };
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(await result.Content.ReadAsStringAsync());
        }
        catch (Exception ex)
        {
            _logger.LogError("Unable to parse data returned from upstream source: {exceptionType}: {exceptionMessage}", ex.GetType().Name, ex.Message);
            throw new EvidenceSourcePermanentServerException(ERROR_UNABLE_TO_PARSE_RESPONSE, "Could not parse the data model returned from upstream source", ex);
        }
    }
}
