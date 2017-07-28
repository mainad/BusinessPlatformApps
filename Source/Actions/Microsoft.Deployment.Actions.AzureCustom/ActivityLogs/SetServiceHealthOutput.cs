﻿using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Actions;
using Microsoft.Deployment.Common.Helpers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Deployment.Actions.AzureCustom.ActivityLogs
{
    [Export(typeof(IAction))]
    public class SetServiceHealthOutput : BaseAction
    {
        public override async Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {
            var azure_access_token = request.DataStore.GetJson("AzureToken", "access_token");
            var subscription = request.DataStore.GetJson("SelectedSubscription", "SubscriptionId");
            var resourceGroup = request.DataStore.GetValue("SelectedResourceGroup");
            var jobName = request.DataStore.GetValue("SAJob");
            string apiVersion = "2015-10-01";
            var uri = $"https://management.azure.com/subscriptions/{subscription}/resourceGroups/{resourceGroup}/providers/Microsoft.StreamAnalytics/streamingjobs/{jobName}/outputs/output?api-version={apiVersion}";
            var uri2 = $"https://main.streamanalytics.ext.azure.com/api/Outputs/PutOutput?fullResourceId=%2Fsubscriptions%2F{subscription}%2FresourceGroups%2F{resourceGroup}%2Fproviders%2FMicrosoft.StreamAnalytics%2Fstreamingjobs%2F{jobName}&subscriptionId={subscription}&resourceGroupName={resourceGroup}&jobName={jobName}&componentType=&componentName=";
            var server = request.DataStore.GetValue("Server");
            var database = request.DataStore.GetValue("Database");
            var user = request.DataStore.GetValue("Username");
            var password = request.DataStore.GetValue("Password");
            var table = request.DataStore.GetValue("ServiceHealthTable");
            var outputAlias = "ServiceHealthOutput-" + RandomGenerator.GetRandomLowerCaseCharacters(5);
            request.DataStore.AddToDataStore("outputAlias", outputAlias);
            var body = $"{{\"properties\":{{\"datasource\":{{\"type\":\"Microsoft.Sql/Server/Database\",\"properties\":{{\"server\":\"{server}\",\"database\":\"{database}\",\"table\":\"{table}\",\"user\":\"{user}\",\"password\":\"{password}\"}}}}}}}}";
            var body3 = $"{{\"properties\":{{\"dataSource\":{{\"outputDocumentDatabaseSource\":{{}},\"outputTopicSource\":{{}},\"outputQueueSource\":{{}},\"outputEventHubSource\":{{}},\"outputSqlDatabaseSource\":{{\"server\":{server},\"database\":{database},\"user\":{user},\"password\":{password},\"table\":{table}}},\"outputBlobStorageSource\":{{}},\"outputTableStorageSource\":{{}},\"outputPowerBISource\":{{}},\"outputDataLakeSource\":{{}},\"outputIotGatewaySource\":{{}},\"type\":\"Microsoft.Sql/Server/Database\"}},\"serialization\":{{}}}},\"createType\":\"None\",\"id\":null,\"location\":\"Australia East\",\"name\":{outputAlias},\"type\":\"Microsoft.Sql/Server/Database\"}}";
            AzureHttpClient ahc = new AzureHttpClient(azure_access_token, subscription);
            HttpResponseMessage response = await ahc.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Put, uri, body);
            if (!response.IsSuccessStatusCode)
            {
                for (int i = 0; i < 5; i++)
                {
                    response = await ahc.ExecuteGenericRequestWithHeaderAsync(HttpMethod.Put, uri, body);
                    if (response.IsSuccessStatusCode)
                    {
                        return new ActionResponse(ActionStatus.Success);
                    }
                    Thread.Sleep(4000);
                }
            }
            return response.IsSuccessStatusCode ? new ActionResponse(ActionStatus.Success) : new ActionResponse(ActionStatus.Failure);
        }
    }
}
