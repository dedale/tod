# tod
Tests On-Demand for Jenkins &amp; GitHub Actions

# TODO
## CLI
- Optional config & workspace paths
- list verb for a user
- status verb for a request

## Core
- Timeout support for FileLock

## Jenkins
- Generic triggering of jobs
- Support complex job dependency graphs
- Support job renaming
- Multiple changesets support (identify the right one containing the files to test)
- Hardcoded build count in JenkinsClient
- Serialization UT: ensure that json converters are needed
- Support lost commits? (not in any root builds)
- Better support for test builds timeouts (missing UT) (use next build?)
- Ignore test builds without tests
- Identify flaky tests across builds, use them in test diff reporting
- Handle JobGroup generation failure
- Handle trailing slash in Jenkins URL

## Workspace
- Serialization UT: ensure that json converters are needed

## Requests
- Transactional triggering of requests, safe resuming without double triggering
- Add user, email
- ChainStatus is wrong (TestTriggered when tests are done but ref still pending)
- Report generation with failed tests diff
- GANTT diagram in report
- Abandon requests upon user request (then stop triggering their builds)
- Archive done requests
- Improve performance (if needed) when looking for requests to update
- Force new root build for a request (retrigger all its builds)
- Include stack, output in failed test? (and reports?)

## git
- Sha1 validation in ctor
- Gerrit Review support
- Check local commit has been pushed (or push it automatically?)
- Hardcoded git commit count in history

## Agent
- Agent mode to automatically synchronize workspace and trigger requests periodically

## Metrics
- ELK support (customizable in config)

## Tod Tests
- Remove NextBuildNumber limit and improve UTs that fail with the same build number
- Coverage step

## GitHub Actions
- Start with a skeleton repo
- Lots of impacts
