// MatchShift on Azure: Container Apps + Container Registry + Log Analytics + Application Insights,
// with a system-assigned managed identity that can call a Microsoft Foundry model deployment
// (keyless, via Microsoft Entra ID). Foundry is optional - without it the app uses templates.
targetScope = 'resourceGroup'

@description('Environment name, used as a prefix for resources (azd sets this).')
param environmentName string

param location string = resourceGroup().location

@description('Microsoft Foundry OpenAI-compatible endpoint, e.g. https://<resource>.services.ai.azure.com/openai/v1. Empty disables AI narratives.')
param foundryEndpoint string = ''

@description('Model deployment name in Microsoft Foundry.')
param foundryDeployment string = ''

@description('Resource ID of the Foundry / Azure AI Services account, to grant the app keyless access. Optional.')
param foundryResourceId string = ''

@description('Container image; azd replaces this on deploy.')
param image string = 'mcr.microsoft.com/k8se/quickstart:latest'

var token = toLower(uniqueString(subscription().id, resourceGroup().id, environmentName))
var tags = { 'azd-env-name': environmentName, app: 'matchshift', data: 'synthetic' }

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${token}'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${token}'
  location: location
  tags: tags
  kind: 'web'
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${token}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${token}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: logs.properties.customerId, sharedKey: logs.listKeys().primarySharedKey }
    }
  }
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-matchshift-${token}'
  location: location
  tags: union(tags, { 'azd-service-name': 'web' })
  identity: { type: 'SystemAssigned' }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto' }
      registries: [ { server: registry.properties.loginServer, identity: 'system' } ]
    }
    template: {
      containers: [
        {
          name: 'matchshift'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
            { name: 'Foundry__Enabled', value: empty(foundryEndpoint) ? 'false' : 'true' }
            { name: 'Foundry__Endpoint', value: foundryEndpoint }
            { name: 'Foundry__Deployment', value: foundryDeployment }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 } }
          ]
        }
      ]
      // One replica: the demo playback session lives in memory (see docs/MICROSOFT_INTEGRATION.md for scale-out).
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// Let the app pull from the registry with its managed identity.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, app.id, 'acrpull')
  scope: registry
  properties: {
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// Optional: keyless access to the Foundry model ("Cognitive Services OpenAI User").
module foundryAccess 'foundry-access.bicep' = if (!empty(foundryResourceId)) {
  name: 'foundry-access'
  scope: resourceGroup(split(foundryResourceId, '/')[2], split(foundryResourceId, '/')[4])
  params: {
    accountName: last(split(foundryResourceId, '/'))
    principalId: app.identity.principalId
  }
}

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = registry.properties.loginServer
output SERVICE_WEB_URI string = 'https://${app.properties.configuration.ingress.fqdn}'
output APPLICATIONINSIGHTS_NAME string = insights.name
