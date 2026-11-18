/**
 * How long reports and upload records are kept. Must match Retention:ReportDays in the API
 * (backend/src/Breakbar.Api/appsettings.json) and is what the privacy policy promises
 * (src/pages/PrivacyPage.tsx). Change all three together.
 */
export const REPORT_RETENTION_DAYS = 7
