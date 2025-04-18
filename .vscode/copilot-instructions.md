# MCP Server Development Instructions

These instructions are based on lessons learned from previous MCP server development sessions and should be considered whenever working with Model Context Protocol implementations.

## Session Handling

- **Always extract session IDs properly**: Extract session IDs from query parameters first, with fallbacks to context items, and generate new IDs when none exist.
- **Use a centralized session state**: Store session IDs in a single, static class to maintain consistent access across tools.
- **Prevent null reference exceptions**: Always include null checks when accessing session-related objects.

## Authentication & Headers

- **Implement defensive header access**: Never assume headers will be present; always use null checking and provide fallbacks.
- **Log headers for debugging**: Include code to log headers when debugging authentication issues, but ensure sensitive information is redacted.
- **Test with hardcoded values**: When troubleshooting environment variable issues, use hardcoded values first to validate the flow.

## Authentication & Configuration

- **Always verify configuration before use**: Before using any authentication method or configuration value, verify that it exists and is properly configured in the environment.
- **Implement environment detection**: Use proper environment detection to determine which authentication method (client secret vs. managed identity) to use.
- **Provide clear error messages**: When configuration is missing, provide specific error messages that identify exactly which configuration is missing.
- **Test in local environments first**: Verify that authentication and configuration work in local environments before deploying to production.

## MCP Tool Implementation

- **Understand the MCP protocol flow**: Always review the SDK implementation before making assumptions about how data flows between client and server.
- **Implement comprehensive error handling**: Include try-catch blocks in tool methods and return meaningful error messages.
- **Provide detailed logging**: Log key operations and decision points, including session IDs and authentication status.

## Azure Integration

- **Add fallbacks for Azure services**: When integrating with Azure Blob Storage or other services, provide local fallbacks for development and testing.
- **Secure credential management**: Follow best practices for handling Azure credentials, using managed identities where possible.
- **Validate configuration early**: Check for required configuration values at startup rather than when the tool is invoked.

## General Coding Guidelines

- **Study framework code first**: When working with unfamiliar frameworks like MCP, take time to understand the underlying implementation.
- **Document tool parameters clearly**: Use descriptive attributes to document parameters and return values.
- **Avoid hard assumptions**: Don't assume environment variables will be properly expanded or that authentication will always work.
- **Verify code currency before modification**: Before modifying any code, ensure you're working with the most current version. Never rely on cached or outdated representations of the codebase.
- **Confirm tool availability and functionality**: Before using tool functions, verify they exist and are properly configured in the current environment.

## Code Modification Discipline

- **STRICTLY limit changes to what was requested**: Never modify code that wasn't explicitly mentioned in the user's request.
- **DO NOT create new files without permission**: Never create new files or modify existing files unless explicitly instructed.
- **NO unsolicited improvements**: Do not attempt to "fix" or "improve" code that wasn't part of the request, even if you believe it contains issues.
- **ASK before expanding scope**: If you believe additional changes might be beneficial, ask explicitly rather than implementing them.
- **RESPECT the user's ownership**: Remember that the user has full knowledge of their codebase and development process - your role is to assist only with what is explicitly requested.
- **MAINTAIN LASER FOCUS on the explicit task**: When asked to modify one function or component, never modify related functions or components without explicit permission, even if they seem related or similar. Each change must be specifically requested.
- **VALIDATE CONFIGURATION before implementing**: Before implementing code that relies on specific configuration values, verify that those values are available in the expected environment. Don't assume configuration exists without checking.
- **NEVER modify code based on outdated understanding**: When working with a codebase that may have evolved, always re-read and re-analyze relevant files before suggesting changes.
- **CONFIRM authentication methods are appropriate**: Different environments (local vs. cloud) may require different authentication methods; always verify the appropriate method is being used.

---

*These instructions were generated based on lessons learned from previous development sessions and should be updated as new patterns and anti-patterns are identified.*