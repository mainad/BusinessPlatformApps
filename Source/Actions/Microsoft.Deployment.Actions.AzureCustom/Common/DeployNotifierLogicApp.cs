﻿using Microsoft.Deployment.Common.Actions;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Helpers;
using Microsoft.Azure;
using System.IO;
using Microsoft.Azure.Management.Resources;
using Microsoft.Azure.Management.Resources.Models;
using System.Threading;
using Microsoft.Deployment.Common.ErrorCode;
using System.Net.Http;
using System.Dynamic;

namespace Microsoft.Deployment.Actions.AzureCustom.Common
{
    [Export(typeof(IAction))]
    public class DeployNotifierLogicApp : BaseAction
    {
        public override async Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {
            string azureToken = request.DataStore.GetJson("AzureToken", "access_token");
            string subscription = request.DataStore.GetJson("SelectedSubscription", "SubscriptionId");
            string resourceGroup = request.DataStore.GetValue("SelectedResourceGroup");
            string doNotWaitString = request.DataStore.GetValue("Wait");

            string logicAppName = string.Empty;

            bool doNotWait = !string.IsNullOrEmpty(doNotWaitString) && bool.Parse(doNotWaitString);
            string deploymentName = request.DataStore.GetValue("DeploymentName");

            // Read from file
            var armTemplatefilePath = "Service/Notifier/notifierLogicApp.json";
            var armParamTemplateProperties = request.DataStore.GetJson("AzureArmParameters");

            if (deploymentName == null && !doNotWait)
            {
                deploymentName = request.DataStore.CurrentRoute;
            }

            var param = new AzureArmParameterGenerator();
            foreach (var prop in armParamTemplateProperties.Children())
            {
                string key = prop.Path.Split('.').Last();
                string value = prop.First().ToString();

                param.AddStringParam(key, value);

                if(key == "logicAppName")
                {
                    logicAppName = value;
                }
            }

            var armTemplate = JsonUtility.GetJObjectFromJsonString(System.IO.File.ReadAllText(Path.Combine(request.Info.App.AppFilePath, armTemplatefilePath)));
            var armParamTemplate = JsonUtility.GetJObjectFromObject(param.GetDynamicObject());
            armTemplate.Remove("parameters");
            armTemplate.Add("parameters", armParamTemplate["parameters"]);

            SubscriptionCloudCredentials creds = new TokenCloudCredentials(subscription, azureToken);
            Microsoft.Azure.Management.Resources.ResourceManagementClient client = new ResourceManagementClient(creds);


            var deployment = new Microsoft.Azure.Management.Resources.Models.Deployment()
            {
                Properties = new DeploymentPropertiesExtended()
                {
                    Template = armTemplate.ToString(),
                    Parameters = JsonUtility.GetEmptyJObject().ToString()
                }
            };

            var validate = await client.Deployments.ValidateAsync(resourceGroup, deploymentName, deployment, new CancellationToken());
            if (!validate.IsValid)
                return new ActionResponse(
                    ActionStatus.Failure,
                    JsonUtility.GetJObjectFromObject(validate),
                    null,
                    DefaultErrorCodes.DefaultErrorCode,
                    $"Azure:{validate.Error.Message} Details:{validate.Error.Details}");

            var deploymentItem = await client.Deployments.CreateOrUpdateAsync(resourceGroup, deploymentName, deployment, new CancellationToken());

            if (doNotWait)
            {
                return new ActionResponse(ActionStatus.Success, deploymentItem);
            }

            var resp = await WaitForAction(client, resourceGroup, deploymentName);

            AzureHttpClient azureClient = new AzureHttpClient(azureToken, subscription, resourceGroup);
            var response = await azureClient.ExecuteWithSubscriptionAndResourceGroupAsync(HttpMethod.Post, $"providers/Microsoft.Logic/workflows/{logicAppName}/triggers/manual/listCallbackUrl", "2016-06-01", string.Empty);
            if (!response.IsSuccessStatusCode)
            {
                return new ActionResponse(ActionStatus.Failure);
            }

            var postUrl = JsonUtility.GetJObjectFromJsonString(await response.Content.ReadAsStringAsync());

            //response = await azureClient.ExecuteGenericRequestNoHeaderAsync(HttpMethod.Post, postUrl["value"].ToString(), string.Empty);

            dynamic newParam = new ExpandoObject();
            newParam.notifierUrl = postUrl["value"];
            newParam.logicAppName = logicAppName;
            newParam.sqlConnection = request.DataStore.GetValue("sqlConnectionName");
            newParam.resourcegroup = resourceGroup;
            newParam.subscription = subscription;
            armParamTemplate = JsonUtility.GetJObjectFromObject(newParam);
            armTemplate.Remove("parameters");
            armTemplate.Add("parameters", armParamTemplate);

            deployment = new Microsoft.Azure.Management.Resources.Models.Deployment()
            {
                Properties = new DeploymentPropertiesExtended()
                {
                    Template = armTemplate.ToString(),
                    Parameters = JsonUtility.GetEmptyJObject().ToString()
                }
            };

            validate = await client.Deployments.ValidateAsync(resourceGroup, deploymentName, deployment, new CancellationToken());
            if (!validate.IsValid)
                return new ActionResponse(
                    ActionStatus.Failure,
                    JsonUtility.GetJObjectFromObject(validate),
                    null,
                    DefaultErrorCodes.DefaultErrorCode,
                    $"Azure:{validate.Error.Message} Details:{validate.Error.Details}");

            deploymentItem = await client.Deployments.CreateOrUpdateAsync(resourceGroup, deploymentName, deployment, new CancellationToken());

            return await WaitForAction(client, resourceGroup, deploymentName);
        }

        private async Task<ActionResponse> WaitForAction(ResourceManagementClient client, string resourceGroup, string deploymentName)
        {
            while (true)
            {
                Thread.Sleep(5000);
                var status = await client.Deployments.GetAsync(resourceGroup, deploymentName, new CancellationToken());
                var operations =
                    await
                        client.DeploymentOperations.ListAsync(resourceGroup, deploymentName,
                            new DeploymentOperationsListParameters(), new CancellationToken());
                var provisioningState = status.Deployment.Properties.ProvisioningState;

                if (provisioningState == "Accepted" || provisioningState == "Running")
                    continue;

                if (provisioningState == "Succeeded")
                    return new ActionResponse(ActionStatus.Success, operations);

                var operation = operations.Operations.First(p => p.Properties.ProvisioningState == ProvisioningState.Failed);
                var operationFailed =
                    await
                        client.DeploymentOperations.GetAsync(resourceGroup, deploymentName, operation.OperationId,
                            new CancellationToken());

                return new ActionResponse(ActionStatus.Failure, operationFailed);
            }
        }
    }
}
