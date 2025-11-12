# Content Moderation Policy

**Version:** 1.0.0  
**Last Updated:** 2025-01-27  
**Owner:** PoSeeReview Development Team

## Overview

PoSeeReview generates comic reviews of restaurants using AI. To maintain a safe, inclusive, and family-friendly platform, we implement content moderation to filter inappropriate content from user inputs and AI-generated outputs.

## Scope

This policy applies to:
- User-submitted restaurant names and addresses
- User-submitted location queries (city names, ZIP codes)
- AI-generated comic panel descriptions
- AI-generated comic images
- User-generated share links and metadata

## Content Moderation Rules

### 1. Profanity Filtering

**Objective:** Prevent offensive language in user inputs and AI outputs.

**Implementation:**
- **Level:** Medium (block common profanity, allow mild expressions)
- **User Inputs:** Validate restaurant names, location queries before submission
- **AI Outputs:** Scan comic descriptions and alt text before image generation
- **Action:** Reject content, return error message to user

**Blocked Content:**
- Strong profanity and vulgar language
- Sexual explicit language
- Slurs and derogatory terms
- Obscene gestures or expressions

**Allowed Content:**
- Mild expressions (e.g., "darn", "heck")
- Restaurant names with potentially sensitive words in legitimate context (e.g., "The Damn Good Burger")

**Technical Approach:**
```csharp
// Azure AI Content Safety - Text Analysis
var response = await contentSafetyClient.AnalyzeTextAsync(new AnalyzeTextOptions
{
    Text = userInput,
    Categories = { TextCategory.Profanity },
    Severity = Severity.Medium // Block medium and high severity
});

if (response.Value.ProfanityResult.Severity >= Severity.Medium)
{
    throw new ContentModerationException("Input contains inappropriate language");
}
```

**Error Message:**
> "We couldn't process your request because it contains inappropriate language. Please try again with different wording."

---

### 2. Hate Speech Filtering

**Objective:** Prevent discriminatory, hateful, or harmful content targeting protected groups.

**Implementation:**
- **Level:** Low (block any hate speech, zero tolerance)
- **User Inputs:** Scan all text inputs before processing
- **AI Outputs:** Verify AI-generated descriptions don't contain bias or stereotypes
- **Action:** Reject content, log incident for review

**Blocked Content:**
- Hate speech targeting race, ethnicity, religion, gender, sexual orientation, disability
- Discriminatory language or stereotypes
- Incitement to violence or harm
- Symbols or references associated with hate groups

**Allowed Content:**
- Discussions about food from different cultures (positive context)
- Historical or educational context (with appropriate framing)

**Technical Approach:**
```csharp
// Azure AI Content Safety - Hate Speech Analysis
var response = await contentSafetyClient.AnalyzeTextAsync(new AnalyzeTextOptions
{
    Text = userInput,
    Categories = { TextCategory.Hate },
    Severity = Severity.Low // Zero tolerance - block any hate speech
});

if (response.Value.HateResult.Severity >= Severity.Low)
{
    await auditLogger.LogIncidentAsync(new ModerationIncident
    {
        Type = "HateSpeech",
        Content = userInput,
        Timestamp = DateTime.UtcNow
    });
    
    throw new ContentModerationException("Content violates our community guidelines");
}
```

**Error Message:**
> "Your request violates our community guidelines. We have a zero-tolerance policy for hate speech and discriminatory content."

---

### 3. Explicit Content Filtering

**Objective:** Maintain a family-friendly platform by blocking sexually explicit content and graphic violence.

**Implementation:**
- **Level:** Medium (block explicit content, allow mild suggestive content in food context)
- **User Inputs:** Scan text and image references
- **AI Outputs:** Analyze generated images before displaying to users
- **Action:** Reject content, suggest alternative phrasing

**Blocked Content:**
- Sexually explicit content (nudity, sexual acts, adult content)
- Graphic violence or gore
- Drug-related content (illegal substances)
- Self-harm or suicide content

**Allowed Content:**
- Food imagery (e.g., meat preparation, seafood displays)
- Mild suggestive food names in legitimate restaurant context (e.g., "Sexy Sushi")
- Wine, beer, cocktail references (legal alcohol in restaurant context)

**Technical Approach:**
```csharp
// Azure AI Content Safety - Multi-Category Analysis
var textResponse = await contentSafetyClient.AnalyzeTextAsync(new AnalyzeTextOptions
{
    Text = userInput,
    Categories = 
    { 
        TextCategory.Sexual, 
        TextCategory.Violence,
        TextCategory.SelfHarm 
    },
    Severity = Severity.Medium
});

if (textResponse.Value.SexualResult.Severity >= Severity.Medium ||
    textResponse.Value.ViolenceResult.Severity >= Severity.Medium ||
    textResponse.Value.SelfHarmResult.Severity >= Severity.Medium)
{
    throw new ContentModerationException("Content contains explicit material");
}

// For AI-generated images
var imageResponse = await contentSafetyClient.AnalyzeImageAsync(new AnalyzeImageOptions
{
    ImageContent = BinaryData.FromBytes(imageBytes),
    Categories = 
    { 
        ImageCategory.Sexual, 
        ImageCategory.Violence,
        ImageCategory.SelfHarm 
    },
    Severity = Severity.Medium
});

if (imageResponse.Value.SexualResult.Severity >= Severity.Medium ||
    imageResponse.Value.ViolenceResult.Severity >= Severity.Medium ||
    imageResponse.Value.SelfHarmResult.Severity >= Severity.Medium)
{
    await auditLogger.LogIncidentAsync(new ModerationIncident
    {
        Type = "ExplicitImage",
        ImageUrl = imageUrl,
        Timestamp = DateTime.UtcNow
    });
    
    throw new ContentModerationException("Generated content violates content policy");
}
```

**Error Message:**
> "We couldn't generate a comic for this request because it contains explicit content. Please try a different restaurant or search query."

---

## Azure AI Content Safety Integration

### Service Configuration

**Resource:**
- Service: Azure AI Content Safety
- Tier: Standard (S0)
- Region: East US (same as Application Insights for latency)

**Endpoint:**
```
https://<resource-name>.cognitiveservices.azure.com/
```

**Authentication:**
- Primary: Azure Key Vault secret reference
- Fallback: Managed Identity (when deployed to Azure App Service)

### Request/Response Flow

1. **User Input Validation:**
   ```
   User Input → Content Safety API → Pass/Fail → Continue/Reject
   ```

2. **AI Output Validation:**
   ```
   AI Prompt → DALL-E API → Generated Image → Content Safety API → Pass/Fail → Display/Reject
   ```

3. **Performance Optimization:**
   - Cache validation results for 1 hour (identical text inputs)
   - Batch multiple validations when possible
   - Skip validation for health check endpoints

### Cost Management

**Pricing (as of 2025-01-27):**
- Text Analysis: $1.00 per 1,000 transactions
- Image Analysis: $1.00 per 1,000 transactions

**Expected Volume:**
- 1,000 users/month × 10 comics each = 10,000 text validations = $10/month
- 10,000 image validations = $10/month
- **Total: ~$20/month**

**Budget Alert:**
- Set alert at $30/month (150% of expected cost)
- Review usage if cost exceeds $50/month

---

## Severity Thresholds

Azure AI Content Safety returns severity levels for each category:

| Severity | Value | Description | Action |
|----------|-------|-------------|--------|
| Safe | 0 | No harmful content detected | Allow |
| Low | 2 | Potentially sensitive, context-dependent | Review case-by-case |
| Medium | 4 | Likely inappropriate | Block |
| High | 6 | Definitely inappropriate | Block + Log |

**PoSeeReview Thresholds:**
- **Profanity:** Block Medium (4) and above
- **Hate Speech:** Block Low (2) and above (zero tolerance)
- **Sexual Content:** Block Medium (4) and above
- **Violence:** Block Medium (4) and above
- **Self-Harm:** Block Low (2) and above (zero tolerance)

---

## Fallback Strategy

If Azure AI Content Safety is unavailable (service outage, quota exceeded):

1. **Client-Side Validation:**
   - Basic profanity filter using word list
   - Length limits (max 500 characters)
   - Character restrictions (alphanumeric + common punctuation)

2. **AI Safety Instructions:**
   - Add content policy to DALL-E prompts:
     ```
     "Generate a family-friendly, appropriate, non-violent comic panel..."
     ```

3. **Manual Review Queue:**
   - Log all content when service is unavailable
   - Queue for manual review within 24 hours
   - Notify users: "Your content is under review"

4. **Circuit Breaker:**
   - After 3 consecutive failures, switch to fallback mode
   - Reset after 5 minutes of successful responses

---

## Audit and Compliance

### Logging

**Logged Events:**
- All content moderation rejections (category, severity, timestamp)
- User ID (hashed) for rate limiting
- Original content (hashed or truncated for privacy)
- Action taken (rejected, queued for review)

**Retention:**
- Audit logs: 90 days in Azure Table Storage
- High-severity incidents: 1 year (compliance requirement)

**Log Format:**
```json
{
  "eventId": "mod-20250127-1234567",
  "timestamp": "2025-01-27T14:30:00Z",
  "userId": "hash-abc123",
  "category": "HateSpeech",
  "severity": "Medium",
  "action": "Rejected",
  "contentHash": "sha256-xyz789",
  "ipAddress": "203.0.113.42"
}
```

### Monitoring

**Key Metrics:**
- Moderation rejection rate (target: < 5% of requests)
- False positive rate (target: < 1%)
- API latency (target: < 500ms P95)
- API availability (target: 99.9%)

**Alerts:**
- Rejection rate > 10% (potential attack or service misconfiguration)
- API latency > 1 second (performance degradation)
- API errors > 5% (service outage)

**Dashboard:**
- KQL queries available in `docs/kql/content-moderation-metrics.kql`

---

## Review and Appeals

### User Appeals

Users can appeal content moderation decisions:

1. **Email:** support@poseereview.com
2. **Subject:** "Content Appeal - [Event ID]"
3. **Review SLA:** 48 hours

**Appeal Process:**
1. Review original content and moderation decision
2. Consult Azure AI Content Safety details
3. Manual override if false positive
4. Update word lists/configurations if needed
5. Notify user of decision

### Regular Review

**Monthly:**
- Review top 20 false positives
- Update word lists and severity thresholds
- Analyze moderation trends

**Quarterly:**
- Review content policy effectiveness
- Update Azure AI Content Safety configuration
- Train team on edge cases

**Annually:**
- Full policy review and update
- Compliance audit
- Update documentation

---

## Edge Cases and Exceptions

### False Positives

**Scenario:** Restaurant name "The Smoking Gun BBQ" flagged for violence/tobacco  
**Resolution:** Whitelist legitimate restaurant names  
**Implementation:** Maintain allow-list in Azure Table Storage

### Cultural Context

**Scenario:** Foreign language input (e.g., Spanish restaurant name)  
**Resolution:** Azure AI Content Safety supports 100+ languages  
**Implementation:** Pass language code if known, auto-detect otherwise

### Medical/Scientific Terms

**Scenario:** "Butcher's Block" flagged for violence  
**Resolution:** Context-aware moderation (food industry terms allowed)  
**Implementation:** Custom word list for food-related terms

### Historical Content

**Scenario:** Restaurant named after historical figure with controversial past  
**Resolution:** Allow historical names in neutral context  
**Implementation:** Manual review queue for edge cases

---

## Privacy and Data Protection

### User Data

- **PII:** Never log personally identifiable information in moderation logs
- **Content:** Hash or truncate user content before logging
- **IP Addresses:** Anonymize after 30 days
- **GDPR:** Support data deletion requests (delete all logs for user ID)

### AI Training

- **Opt-Out:** User content is NOT used to train Azure AI models (per Azure policy)
- **Data Residency:** All data processed in East US region
- **Compliance:** GDPR, CCPA, SOC 2 compliant

---

## Contact and Support

**Policy Questions:**  
- Email: policy@poseereview.com

**Technical Implementation:**  
- Email: dev@poseereview.com

**User Appeals:**  
- Email: support@poseereview.com

**Incident Reporting:**  
- Email: security@poseereview.com (for security incidents)

---

## Document Version History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0.0 | 2025-01-27 | Development Team | Initial policy creation |

---

## References

- [Azure AI Content Safety Documentation](https://learn.microsoft.com/en-us/azure/ai-services/content-safety/)
- [PoSeeReview Constitution v2.0.0](../specs/002-constitution-compliance/constitution.md)
- [Content Moderation KQL Queries](../docs/kql/content-moderation-metrics.kql)
- [Content Moderation Architecture](../docs/diagrams/c4-component.mmd)
