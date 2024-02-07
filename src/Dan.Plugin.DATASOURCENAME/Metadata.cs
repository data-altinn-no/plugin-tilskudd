using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Dan.Common;
using Dan.Common.Enums;
using Dan.Common.Interfaces;
using Dan.Common.Models;
using Dan.Plugin.DATASOURCENAME.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Newtonsoft.Json.Schema.Generation;

namespace Dan.Plugin.DATASOURCENAME;

/// <summary>
/// All plugins must implement IEvidenceSourceMetadata, which describes that datasets returned by this plugin. An example is implemented below.
/// </summary>
public class Metadata : IEvidenceSourceMetadata
{
    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public List<EvidenceCode> GetEvidenceCodes()
    {
        JSchemaGenerator generator = new JSchemaGenerator();

        return new List<EvidenceCode>()
        {
            new()
            {
                EvidenceCodeName = global::Dan.Plugin.DATASOURCENAME.Plugin.SimpleDatasetName,
                EvidenceSource = global::Dan.Plugin.DATASOURCENAME.Plugin.SourceName,
                Values = new List<EvidenceValue>()
                {
                    new()
                    {
                        EvidenceValueName = "field1",
                        ValueType = EvidenceValueType.String
                    },
                    new()
                    {
                        EvidenceValueName = "field2",
                        ValueType = EvidenceValueType.String
                    }
                }
            },
            new()
            {
                EvidenceCodeName = global::Dan.Plugin.DATASOURCENAME.Plugin.RichDatasetName,
                EvidenceSource = global::Dan.Plugin.DATASOURCENAME.Plugin.SourceName,
                Values = new List<EvidenceValue>()
                {
                    new()
                    {
                        // Convention for rich datasets with a single JSON model is to use the value name "default"
                        EvidenceValueName = "default",
                        ValueType = EvidenceValueType.JsonSchema,
                        JsonSchemaDefintion =  generator.Generate(typeof(ExampleModel)).ToString()
                    }
                },
                AuthorizationRequirements = new List<Requirement>
                {
                    new MaskinportenScopeRequirement
                    {
                        RequiredScopes = new List<string> { "altinn:dataaltinnno/somescope" }
                    }
                }
            }
        };
    }


    /// <summary>
    /// This function must be defined in all DAN plugins, and is used by core to enumerate the available datasets across all plugins.
    /// Normally this should not be changed.
    /// </summary>
    /// <param name="req"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    [Function(Constants.EvidenceSourceMetadataFunctionName)]
    public async Task<HttpResponseData> GetMetadataAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req,
        FunctionContext context)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(GetEvidenceCodes());
        return response;
    }

}
