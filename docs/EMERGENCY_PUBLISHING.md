# Emergency Package Publishing

This repository includes an emergency package publishing workflow that allows the repository owner to immediately publish packages to both NuGet.org and GitHub Packages, bypassing all normal change detection and validation mechanisms.

## 🚨 When to Use Emergency Publishing

Emergency publishing should only be used in critical situations such as:

- **Critical Security Fixes**: When a security vulnerability needs immediate patching
- **Production Hotfixes**: When a critical bug in production requires immediate resolution
- **Service Outages**: When the main CI/CD pipeline is down but a package needs to be published
- **Time-Sensitive Releases**: When normal release processes would cause unacceptable delays

## 🔒 Access Control

**IMPORTANT**: Only the repository owner (`schivei`) can execute the emergency publishing workflow. Any attempt by other users will be automatically rejected.

## 🚀 How to Use Emergency Publishing

### Step 1: Navigate to Actions
1. Go to the repository's **Actions** tab
2. Find the "Emergency Package Publishing" workflow in the left sidebar

### Step 2: Trigger Manual Execution
1. Click on "Emergency Package Publishing"
2. Click the **"Run workflow"** button (top right)
3. Fill in the required information:
   - **Branch**: Ensure you're running from the correct branch (usually `main`)
   - **Reason for emergency publishing**: Provide a clear explanation of why emergency publishing is needed

### Step 3: Monitor Execution
1. The workflow will first verify that you are the repository owner
2. If authorized, it will proceed with:
   - Building the package
   - Running tests
   - Creating NuGet packages
   - Publishing to NuGet.org
   - Publishing to GitHub Packages

### Step 4: Verify Publishing
1. Check the workflow logs for success confirmation
2. Verify the package appears on:
   - [NuGet.org](https://www.nuget.org/packages/NetMediate/)
   - [GitHub Packages](https://github.com/schivei/net-mediate/packages)

## ⚠️ Important Notes

### Bypassed Safeguards
This workflow intentionally bypasses the following normal safeguards:
- **Change Detection**: Publishes regardless of whether source code has changed
- **Code Formatting Checks**: Does not validate CSharpier formatting
- **Documentation Checks**: Does not validate XML documentation completeness
- **PR Requirements**: Does not require pull request review process

### What Still Runs
The following safeguards are still enforced:
- **Build Validation**: The package must build successfully
- **Test Execution**: All tests must pass
- **Access Control**: Only the repository owner can execute

### Audit Trail
Every emergency publish execution creates an audit trail including:
- Who triggered the publish
- When it was triggered
- The reason provided
- Complete build and publish logs

## 🛡️ Security Considerations

- The workflow requires the same secrets as normal publishing (`NUGET_API_KEY`, `GITHUB_TOKEN`)
- Access is restricted at the workflow level, not just through GitHub permissions
- All executions are logged and auditable
- The workflow is independent of the main CI/CD pipeline to prevent interference

## 📋 Troubleshooting

### Access Denied Error
If you receive an "Access denied" error, ensure that:
- You are logged in as the repository owner (`schivei`)
- You are executing the workflow from the correct repository

### Build Failures
If the build fails during emergency publishing:
- Check the build logs for specific error messages
- Ensure the code on the selected branch is in a buildable state
- Verify all dependencies are available

### Publishing Failures
If publishing fails:
- Check that the required secrets (`NUGET_API_KEY`) are properly configured
- Verify network connectivity to NuGet.org and GitHub Packages
- Check for API key expiration or permission issues

## 📞 Support

For issues with emergency publishing, contact the repository owner at [schivei@icloud.com](mailto:schivei@icloud.com).