// Azure Budget Alert for PoSeeReview Resource Group
// Limits monthly spending to $5 with email notifications at 80% and 100%

param budgetName string = 'PoSeeReview-Monthly-Budget'
param monthlyLimit int = 5
param contactEmails array = []
param startDate string = utcNow('yyyy-MM-01') // First day of current month

// Calculate end date (1 year from now)
var endDate = dateTimeAdd(startDate, 'P1Y', 'yyyy-MM-dd')

// Budget resource - only deploy if contact emails provided
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = if (length(contactEmails) > 0) {
  name: budgetName
  properties: {
    timePeriod: {
      startDate: startDate
      endDate: endDate
    }
    timeGrain: 'Monthly'
    amount: monthlyLimit
    category: 'Cost'
    notifications: {
      // Alert at 80% of monthly budget ($4.00)
      Warning80: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: contactEmails
        thresholdType: 'Actual'
      }
      // Alert at 100% of monthly budget ($5.00)
      Critical100: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: contactEmails
        thresholdType: 'Actual'
      }
    }
  }
}

output budgetId string = length(contactEmails) > 0 ? budget.id : ''
output budgetName string = length(contactEmails) > 0 ? budget.name : ''
