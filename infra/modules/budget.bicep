// Azure Budget Alert for PoSeeReview Resource Group
// Limits daily spending to $2 with email notifications

param budgetName string = 'PoSeeReview-Daily-Budget'
param dailyLimit int = 2
param notificationEmail string
param startDate string = utcNow('yyyy-MM-dd')

// Calculate end date (1 year from now)
var endDate = dateTimeAdd(startDate, 'P1Y', 'yyyy-MM-dd')

// Budget resource
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: budgetName
  properties: {
    timePeriod: {
      startDate: startDate
      endDate: endDate
    }
    timeGrain: 'Daily'
    amount: dailyLimit
    category: 'Cost'
    notifications: {
      // Alert at 80% of daily budget ($1.60)
      'Warning80': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: [
          notificationEmail
        ]
        thresholdType: 'Actual'
      }
      // Alert at 100% of daily budget ($2.00)
      'Critical100': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: [
          notificationEmail
        ]
        thresholdType: 'Actual'
      }
      // Alert at 120% of daily budget ($2.40)
      'Exceeded120': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 120
        contactEmails: [
          notificationEmail
        ]
        thresholdType: 'Actual'
      }
    }
  }
}

output budgetId string = budget.id
output budgetName string = budget.name
