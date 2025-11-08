#!/bin/bash
# Create Azure Budget Alert with Email Notifications
# Complete setup including Action Group

# Configuration
RESOURCE_GROUP="PoSeeReview"
BUDGET_NAME="PoSeeReview-Daily-Budget"
DAILY_LIMIT=2
ALERT_EMAIL="your.email@example.com"  # REPLACE WITH YOUR EMAIL
ACTION_GROUP_NAME="BudgetAlertGroup"
LOCATION="eastus"

echo "🔐 Logging in to Azure..."
az login

# Get subscription ID
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
echo "✅ Using subscription: $SUBSCRIPTION_ID"

# Create Action Group for email notifications
echo "📧 Creating Action Group for email alerts..."
az monitor action-group create \
    --name "$ACTION_GROUP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --short-name "BudgetAlrt" \
    --email-receiver \
        name="BudgetOwner" \
        email-address="$ALERT_EMAIL" \
        use-common-alert-schema true

ACTION_GROUP_ID=$(az monitor action-group show \
    --name "$ACTION_GROUP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --query id -o tsv)

echo "✅ Action Group created: $ACTION_GROUP_ID"

# Set dates
START_DATE=$(date +%Y-%m-%d)
END_DATE=$(date -d "+1 year" +%Y-%m-%d)

# Create budget
echo "💰 Creating daily budget of \$$DAILY_LIMIT..."
az consumption budget create \
    --budget-name "$BUDGET_NAME" \
    --category Cost \
    --amount $DAILY_LIMIT \
    --time-grain Daily \
    --start-date "$START_DATE" \
    --end-date "$END_DATE" \
    --resource-group-filter "$RESOURCE_GROUP" \
    --subscription "$SUBSCRIPTION_ID"

echo ""
echo "✅ Budget created successfully!"
echo ""
echo "📊 View your budget:"
echo "https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/budgets"
echo ""
echo "⚠️  Note: You'll need to add notification rules manually in the portal:"
echo "1. Go to the budget in Azure Portal"
echo "2. Click 'Notifications'"
echo "3. Add alerts at 80%, 100%, and 120% thresholds"
echo "4. Select the Action Group: $ACTION_GROUP_NAME"
