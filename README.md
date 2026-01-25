# Tod

[![Build and Test](https://github.com/dedale/tod/actions/workflows/build-test.yml/badge.svg)](https://github.com/dedale/tod/actions/workflows/build-test.yml)
[![Code Coverage](https://codecov.io/gh/dedale/tod/branch/main/graph/badge.svg)](https://codecov.io/gh/dedale/tod)
[![NuGet](https://img.shields.io/nuget/v/Tod.svg)](https://www.nuget.org/packages/Tod/)

Tod is a command-line tool for running tests on-demand on Jenkins. It helps you trigger and manage Jenkins builds based on your Git commits and custom test filters, making CI/CD workflows more efficient.

## Features

- 🚀 **On-Demand Test Execution** - Trigger Jenkins builds for specific commits
- 🔍 **Smart Branch Detection** - Automatically identifies the correct reference branch
- 📊 **Build Tracking** - Monitors and synchronizes build status
- 📧 **Email Reports** - Sends build results via email
- 🎯 **Filter-Based Job Selection** - Use regex patterns to select which jobs to run
- 💾 **Local Workspace** - Caches build history for faster operations

## Installation

### As a .NET Tool (Recommended)
```
dotnet tool install --global Tod
```

### From Source
```
git clone https://github.com/dedale/tod.git
cd tod
dotnet build
dotnet pack src/Tod/Tod.csproj --configuration Release
dotnet tool install --global --add-source ./src/Tod/bin/packages Tod
```

## Quick Start

### 1. Create a Configuration File

Create a `jenkins_config.json` file with your Jenkins settings:

```json
{
  "url": "https://jenkins.example.com",
  "multiBranchFolders": ["MyProject"],
  "referenceJobs": [
    {
      "pattern": "^MAIN-(?<root>build)$",
      "branch": "main",
      "isRoot": true
    },
    {
      "pattern": "^MAIN-(?<test>.)$",
      "branch": "main",
      "isRoot": false
    }
  ],
  "onDemandJobs": [
    {
      "pattern": "CUSTOM-(?<root>build)$",
      "isRoot": true
    },
    {
      "pattern": "CUSTOM-(?<test>.)$",
      "isRoot": false
    }
  ],
  "rootFilters": [
    {
      "name": "build",
      "pattern": "^build$"
    }
  ],
  "chainTestGroup": "chains",
  "testFilters": [
    {
      "name": "unit",
      "pattern": "^unit-tests$",
      "group": "tests"
    },
    {
      "name": "integration",
      "pattern": "^integration-tests$",
      "group": "tests"
    }
  ],
  "mailConfig":
  {
    "smtpServer": "smtp.example.com",
    "fromAddress": "jenkins@example.com"
  },
  "keptDays": 30
}
```

### 2. Initialize Workspace
```
tod sync --config jenkins_config.json --workspace ./workspace --jenkins-token YOUR_JENKINS_TOKEN --jobs
```

### 3. Create a Test Request
```
tod new --config jenkins_config.json --workspace ./workspace --branch main --root-filters build --test-filters unit integration --jenkins-token YOUR_JENKINS_TOKEN --gerrit-token YOUR_GERRIT_TOKEN
```

## Configuration File Reference

The Jenkins configuration file (`jenkins_config.json`) defines how Tod interacts with your Jenkins instance.

### Core Settings

| Property | Type | Description |
|----------|------|-------------|
| `url` | string | Jenkins server URL |
| `multiBranchFolders` | string[] | Folders containing multi-branch pipeline jobs |
| `keptDays` | int? | Number of days to keep build history (optional) |
| `maxUserActiveRequests` | int? | Maximum number of active requests per user (optional) |

### Job Patterns

#### Reference Jobs

Define patterns for reference branch jobs (e.g., main, develop):

```json
{
  "pattern": "^MAIN-(?<root>build)$",
  "branch": "main",
  "isRoot": true
}
```

- `pattern`: Regex pattern to match job names
- `branch`: Git branch this job builds
- `isRoot`: `true` for root/build jobs, `false` for test jobs
- Named groups: `(?<root>...)` for root jobs, `(?<test>...)` for test jobs

#### On-Demand Jobs

Define patterns for custom/on-demand jobs:

```json
{
  "pattern": "CUSTOM-(?<root>build)$",
  "isRoot": true
}
```

### Filters

#### Root Filters

Define which root jobs to run:

```json
{
  "name": "build",
  "pattern": "^build$"
}
```

Supports chain patterns with named groups:

```json
{
  "name": "frontend-build",
  "pattern": "^(?<chain>frontend)-build$"
}
```

#### Test Filters

Define which test jobs to run:

```json
{
  "name": "unit",
  "pattern": "^unit-tests$",
  "group": "tests"
}
```

- `name`: Filter identifier
- `pattern`: Regex pattern to match test job names
- `group`: Logical grouping (use `chainTestGroup` value for chain-linked tests)

The `chainTestGroup` property links test filters to root filters via chain patterns.

### Mail Configuration

```json
{
  "smtpServer": "smtp.example.com",
  "fromAddress": "jenkins@example.com"
}
```

### Load Thresholds

Load thresholds protect Jenkins from being overloaded by preventing requests when the server is under high load. Configure thresholds based on queue size and estimated request duration:

```json
{
  "loadThresholds": [
    {
      "queueSize": 50,
      "maxRequestDuration": "01:00:00"
    },
    {
      "queueSize": 100,
      "maxRequestDuration": "00:30:00"
    }
  ]
}
```

**Properties:**
- `queueSize`: Maximum number of builds in Jenkins queue
- `maxRequestDuration`: Maximum total duration for the request (format: "HH:MM:SS")

**How it works:**
- Before registering a new request, Tod checks the current Jenkins queue size
- Tod estimates the total duration of all builds in the request
- If **both** the queue size **and** duration exceed **any** configured threshold, the request is rejected
- Multiple thresholds allow different limits based on load (e.g., stricter limits when queue is larger)

**Example scenarios:**

| Queue Size | Request Duration | Threshold 1 (50, 1h) | Threshold 2 (100, 30min) | Result |
|------------|------------------|----------------------|--------------------------|--------|
| 30 | 45 min | 👍 Both OK | 👍 Queue OK | ✅ Accepted |
| 60 | 75 min | 🔥 Both exceeded | 👍 Queue OK | ❌ Rejected |
| 60 | 20 min | 👍 Duration OK | 👍 Both OK | ✅ Accepted |
| 110 | 35 min | 👍 Duration OK | 🔥 Both exceeded | ❌ Rejected |

**Note:** If no thresholds are configured, all requests are accepted regardless of Jenkins load.

### User Request Limits

The `maxUserActiveRequests` setting limits how many active requests a single user can have running simultaneously. This prevents individual users from overwhelming the Jenkins server with too many concurrent requests.

```json
{
  "maxUserActiveRequests": 3
}
```

**How it works:**
- Before registering a new request, Tod counts the user's currently active requests
- An active request is one that has at least one chain not yet completed
- If the user already has the maximum number of active requests, the new request is rejected
- Completed requests do not count toward the limit
- Each user's limit is tracked independently

**Example scenarios:**
| User Active Requests | Max Limit | Result |
|---------------------|-----------|--------|
| 0 | 3 | ✅ Accepted |
| 2 | 3 | ✅ Accepted |
| 3 | 3 | ❌ Rejected |
| 5 | 3 | ❌ Rejected |

**Note:** If `maxUserActiveRequests` is not configured, users can create unlimited requests.

### Gerrit Integration

When `gerritReviewServer` is configured, Tod verifies that the commit exists in Gerrit before creating a request:

```json
{ "gerritReviewServer": "https://gerrit.example.com" }
```

**How it works:**
- Before triggering builds, Tod queries Gerrit to verify the commit exists as a patchset
- If the commit is not found, the request is rejected with an error
- This prevents Jenkins from failing to checkout code that hasn't been pushed to Gerrit
- Uses the same authentication token as Jenkins by default, or a dedicated Gerrit token if provided via `--gerrit-token`

**Note:** If `gerritReviewServer` is not configured, this check is skipped.

## Commands

### `sync`

Synchronize build history from Jenkins.

#### Sync builds
```
tod sync --config jenkins_config.json --workspace ./workspace --user-token TOKEN
```

#### Sync job list (run this first or when jobs change)
```
tod sync --config jenkins_config.json --workspace ./workspace --user-token TOKEN --jobs
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory
- `-u, --user-token` (required): Jenkins API token
- `-j, --jobs`: Sync job definitions instead of builds

### `new`

Create a new on-demand test request.

```
tod new --config jenkins_config.json --workspace ./workspace --branch main --root-filters build --test-filters unit integration --user-token TOKEN
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory
- `-b, --branch`: Reference branch (auto-detected if not specified)
- `-r, --root-filters` (required): Root filter names to run
- `-t, --test-filters` (required): Test filter names to run
- `-u, --user-token` (required): Jenkins API token

**How it works:**
1. Detects your current Git commit
2. Finds the matching reference build on the specified branch
3. Triggers on-demand builds with your changes
4. Tracks build progress and collects results

### `jobs`

Preview which jobs would be triggered without actually running them.

```
tod jobs --config jenkins_config.json --workspace ./workspace --branch main --root-filters build --test-filters unit integration
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory
- `-b, --branch`: Reference branch
- `-r, --root-filters` (required): Root filter names
- `-t, --test-filters` (required): Test filter names

**Output:**
- Lists root and test jobs that would be triggered
- Shows estimated duration based on historical data

### `report`

Send an email report for a request (completed or not).

```
tod report --config jenkins_config.json --workspace ./workspace --request-id 12345678-1234-1234-1234-123456789abc
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory
- `-i, --request-id` (required): Request UUID to report on

### `abort`

Abort a running or queued request.

```
tod abort --config jenkins_config.json --workspace ./workspace --request-id 12345678-1234-1234-1234-123456789abc
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory
- `-i, --request-id` (required): Request UUID to abort

**Authorization:**
- Users can only abort their own requests
- Returns an error if you try to abort someone else's request

**How it works:**
1. Validates the request ID format
2. Looks up the request in the workspace
3. Verifies you are the owner of the request
4. Marks all chains in the request as aborted
5. Saves the updated request state

**Note:** Aborting a request marks it as complete but does not stop Jenkins builds that are already running. It prevents Tod from tracking those builds further.

### `list`

List your test requests and their status.

```
tod list --config jenkins_config.json --workspace ./workspace
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory
- `-a, --all`: List all requests (including completed ones). By default, only active requests are shown.

**Output:**
For each request belonging to the current user, displays:
- Request ID (UUID)
- Creation timestamp
- Branch and commit
- Test filters used
- Overall status (Active/Done)
- Chain status for each job chain:
  - Root Triggered: Root build has been triggered
  - Tests Triggered: Root build completed, test builds triggered
  - Done: All builds in the chain are complete

**Example output:**
```
Found 2 active requests for user user@example.com:

Request ID: 12345678-1234-1234-1234-123456789abc
  Created: 2024-01-15 10:30:00
  Branch: main
  Commit: abc123def456
  Filters: unit;integration
  Status: Active
    Chain CUSTOM-build: Tests Triggered
    Chain CUSTOM-deploy: Root Triggered

Request ID: 87654321-4321-4321-4321-210987654321
  Created: 2024-01-15 09:15:00
  Branch: develop
  Commit: def456abc123
  Filters: unit
  Status: Done
    Chain CUSTOM-build: Done
```

### `filters`

List all jobs grouped by filters.

```
tod filters --config jenkins_config.json --workspace ./workspace
```

**Options:**
- `-c, --config` (required): Path to Jenkins config file
- `-w, --workspace` (required): Path to workspace directory

**Output:**
- Shows all chains and their associated jobs
- Lists test groups and their jobs
- Reports any configuration errors (unmatched filters, missing jobs)

## Workspace Structure

Tod creates a local workspace to cache build information:

```
workspace/
├── Branches/
│   ├── main/
│   │   ├── Roots/
│   │   │   └── build.json
│   │   └── Tests/
│   │       ├── unit-tests.json
│   │       └── integration-tests.json
│   └── develop/
├── OnDemand/
│   ├── Roots/
│   └── Tests/
├── Requests/
│   └── {request-id}.json
└── Flaky/
    └── flaky-tests.json
```

## Examples

### Example 1: Run Tests for Current Commit

#### First time: sync jobs
```
tod sync -c jenkins.json -w ./workspace -u $JENKINS_TOKEN --jobs
```

#### Sync latest builds
```
tod sync -c jenkins.json -w ./workspace -u $JENKINS_TOKEN
```

#### Create test request
```
tod new -c jenkins.json -w ./workspace -r build -t unit integration -u $JENKINS_TOKEN
```

### Example 2: Target Specific Branch

```
tod new -c jenkins.json -w ./workspace -b develop -r build -t unit -u $JENKINS_TOKEN
```

### Example 3: Check What Would Run

```
tod jobs -c jenkins.json -w ./workspace -r build -t unit integration
```

### Example 4: Validate Filter Configuration

```
tod filters -c jenkins.json -w ./workspace
```

### Example 5: Send Report for a Request
```
tod report -c jenkins.json -w ./workspace -i 12345678-1234-1234-1234-123456789abc
```

### Example 6: Abort a Request

```
tod abort -c jenkins.json -w ./workspace -i 12345678-1234-1234-1234-123456789abc
```

### Example 7: List Your Active Requests

```
# List only active requests
tod list -c jenkins.json -w ./workspace

# List all requests (including completed)
tod list -c jenkins.json -w ./workspace --all
```

## Authentication

Tod requires a Jenkins API token for authentication:

1. Log in to Jenkins
2. Go to **User** → **Configure** → **API Token**
3. Click **Add new Token**
4. Copy the generated token
5. Use it with the `-u` or `--user-token` option

**Security tip:** Store your token in an environment variable:

```
# bash
export JENKINS_TOKEN="your-token-here" tod sync -c jenkins.json -w ./workspace -u $JENKINS_TOKEN

# PowerShell
$env:JENKINS_TOKEN = "your-token-here"
tod sync -c jenkins.json -w ./workspace -u $env:JENKINS_TOKEN

# cmd
set JENKINS_TOKEN=your-token-here
tod sync -c jenkins.json -w .\workspace -u %JENKINS_TOKEN%
```

## Development

### Prerequisites

- .NET 10 SDK or later
- Git
- Visual Studio 2025 or later (recommended)

### Build

```
dotnet restore dotnet build
```

### Run Tests

```
dotnet test
```

### Run with Coverage

```
dotnet test --collect:"XPlat Code Coverage"
```

## Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](.github/CONTRIBUTING.md) for guidelines.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

# TODO

## Core
- Timeout support for FileLock

## Jenkins
- Support complex job dependency graphs
- Support job renaming
- Multiple changesets support (identify the right one containing the files to test) (needed?)
- Hardcoded build count in JenkinsClient
- Serialization UT: ensure that json converters are needed
- Support lost commits? (not in any root builds)
- Better support for test builds timeouts (missing UT) (use next build?)
- Ignore test builds without tests?
- Handle JobGroup generation failure
- Handle trailing slash in Jenkins URL

## Workspace
- Serialization UT: ensure that json converters are needed
- Auto save branch references when adding new ones

## Requests
- Transactional triggering of requests, safe resuming without double triggering
- ChainStatus is wrong (TestTriggered when tests are done but ref still pending)
- GANTT diagram in report
- Archive or purge done requests
- Improve performance (if needed) when looking for requests to update
- Force new root build for a request (retrigger all its builds)
- Storage abstraction for on-demand requests

## git
- Sha1 validation in ctor
- Check local commit has been pushed (or push it automatically?)
- Hardcoded git commit count in history

## Agent
- Agent mode to automatically synchronize workspace and trigger requests periodically

## Tod Tests
- Remove NextBuildNumber limit and improve UTs that fail with the same build number
