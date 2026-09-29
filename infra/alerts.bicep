// Cost and outage alerts for the live app.
//
// Standalone rather than a module of main.bicep: main.bicep names resources from a resourceToken,
// while the live app is app-poseereview in RG PoSeeReview and its telemetry lands in the SHARED
// App Insights component in RG PoShared. Deploy into the app's resource group:
//
//   az deployment group create -g PoSeeReview -f infra/alerts.bicep -p alertEmail=<address>
//
// No availability (ping) test on purpose: the app runs on F1 with no Always On, so a ping every few
// minutes would bill runs AND keep waking a site that is meant to sleep. Http5xx covers failures
// while anyone is using it.

@description('Address that receives every alert.')
param alertEmail string

@description('The App Service that serves the app.')
param siteName string = 'app-poseereview'

@description('Shared Application Insights component the app reports into.')
param appInsightsId string = resourceId('PoShared', 'Microsoft.Insights/components', 'poappideinsights8f9c9a4e')

@description('cloud_RoleName the API stamps on its telemetry (RoleNameTelemetryInitializer).')
param roleName string = 'PoSeeReview.Api'

@description('Paid image generations in 24h that count as a spend spike. ~$0.04 each on Imagen 4.')
param dailyImageThreshold int = 50

@description('Chat spend in 24h (Ai.Cost.Usd) that counts as a spend spike, in USD.')
param dailyChatUsdThreshold string = '1.0'

resource site 'Microsoft.Web/sites@2023-12-01' existing = {
  name: siteName
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-poseereview-email'
  location: 'global'
  properties: {
    groupShortName: 'poseereview'
    enabled: true
    emailReceivers: [
      {
        name: 'admin'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// Same shape as the PoWatch alert: a burst of 5xx usually means a 500.30 startup failure or a
// broken dependency (Key Vault, storage), and on F1 nothing else would say so.
resource http5xx 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'poseereview-http5xx'
  location: 'global'
  properties: {
    description: 'PoSeeReview is returning 5xx responses (possible 500.30 startup failure).'
    severity: 1
    enabled: true
    scopes: [ site.id ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'cond0'
          metricName: 'Http5xx'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroup.id } ]
  }
}

// Images are the dominant cost and are NOT in Ai.Cost.Usd (that metric prices chat tokens only),
// so both are checked. The in-app GenerationBudget caps a day at 500 images; this fires long
// before that ceiling, while there is still time to look.
resource aiSpend 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'poseereview-ai-spend'
  location: resourceGroup().location
  properties: {
    displayName: 'PoSeeReview AI spend spike'
    description: 'More paid image generations or chat spend in the last 24h than normal traffic explains.'
    severity: 2
    enabled: true
    scopes: [ appInsightsId ]
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    criteria: {
      allOf: [
        {
          // Single-quoted, not ''' — Bicep's multi-line strings do not interpolate.
          query: 'customMetrics | where cloud_RoleName == "${roleName}" | where name in ("Gemini.Image.Requests", "Ai.Cost.Usd") | summarize images = sumif(valueSum, name != "Ai.Cost.Usd"), chatUsd = sumif(valueSum, name == "Ai.Cost.Usd") | where images > ${dailyImageThreshold} or chatUsd > ${dailyChatUsdThreshold}'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    // One email per spike, not one an hour for the rest of the day.
    muteActionsDuration: 'PT12H'
    actions: {
      actionGroups: [ actionGroup.id ]
    }
  }
}
