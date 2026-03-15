// Health check routes are fully handled by app.MapHealthChecks in Program.cs:
//   GET /api/health        — full JSON report with all check details and durations
//   GET /api/health/live   — liveness probe (always 200 while process is running)
//   GET /api/health/ready  — readiness probe (checks tagged "ready")
//
// This MVC controller was removed to eliminate the duplicate /api/health route conflict.
namespace Po.SeeReview.Api.Controllers;

