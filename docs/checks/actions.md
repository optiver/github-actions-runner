
# Actions Connection Check

## What is this check for?

Make sure the runner has access to actions service for GitHub.com or GitHub Enterprise Server

- For GitHub.com
  - The runner needs to access `https://api.github.com` for downloading actions.
  - The runner needs to access `https://codeload.github.com` for downloading actions tar.gz/zip.
  - The runner needs to access `https://vstoken.actions.githubusercontent.com/_apis/.../` for requesting an access token.
  - The runner needs to access `https://pipelines.actions.githubusercontent.com/_apis/.../` for receiving workflow jobs.
  - The runner needs to access `https://results-receiver.actions.githubusercontent.com/.../` for reporting progress and uploading logs during a workflow job execution.
  ---
  **NOTE:** for the full list of domains that are required to be in the firewall allow list refer to the [GitHub self-hosted runners requirements documentation](https://docs.github.com/en/actions/hosting-your-own-runners/managing-self-hosted-runners/about-self-hosted-runners#communication-between-self-hosted-runners-and-github).

  These can by tested by running the following `curl` commands from your self-hosted runner machine:

    ```
    curl -v https://api.github.com/zen
    curl -v https://codeload.github.com/_ping
    curl -v https://vstoken.actions.githubusercontent.com/_apis/health
    curl -v https://pipelines.actions.githubusercontent.com/_apis/health
    curl -v https://results-receiver.actions.githubusercontent.com/health
    ```

- For GitHub Enterprise Server
  - The runner needs to access `https://[hostname]/api/v3` for downloading actions.
  - The runner needs to access `https://codeload.[hostname]/_ping` for downloading actions tar.gz/zip.
  - The runner needs to access `https://[hostname]/_services/vstoken/_apis/.../` for requesting an access token.
  - The runner needs to access `https://[hostname]/_services/pipelines/_apis/.../` for receiving workflow jobs.
  
  These can by tested by running the following `curl` commands from your self-hosted runner machine, replacing `[hostname]` with the hostname of your appliance, for instance `github.example.com`:

    ```
    curl -v https://[hostname]/api/v3/zen
    curl -v https://codeload.[hostname]/_ping
    curl -v https://[hostname]/_services/vstoken/_apis/health
    curl -v https://[hostname]/_services/pipelines/_apis/health
    ```

    A common cause of this these connectivity issues is if your to GitHub Enterprise Server appliance is using [the self-signed certificate that is enabled the first time](https://docs.github.com/en/enterprise-server/admin/configuration/configuring-network-settings/configuring-tls) your appliance is started. As self-signed certificates are not trusted by web browsers and Git clients, these clients (including the GitHub Actions runner) will report certificate warnings.
    
    We recommend [upload a certificate signed by a trusted authority](https://docs.github.com/en/enterprise-server/admin/configuration/configuring-network-settings/configuring-tls) to GitHub Enterprise Server, or enabling the built-in ][Let's Encrypt support](https://docs.github.com/en/enterprise-server/admin/configuration/configuring-network-settings/configuring-tls).


## What is checked?

- DNS lookup for api.github.com or myGHES.com using dotnet
- Ping api.github.com or myGHES.com using dotnet
- Make HTTP GET to https://api.github.com or https://myGHES.com/api/v3 using dotnet, check response headers contains `X-GitHub-Request-Id`
---
- DNS lookup for codeload.github.com or codeload.myGHES.com using dotnet
- Ping codeload.github.com or codeload.myGHES.com using dotnet
- Make HTTP GET to https://codeload.github.com/_ping or https://codeload.myGHES.com/_ping using dotnet, check response headers contains `X-GitHub-Request-Id`
---
- DNS lookup for vstoken.actions.githubusercontent.com using dotnet
- Ping vstoken.actions.githubusercontent.com using dotnet
- Make HTTP GET to https://vstoken.actions.githubusercontent.com/_apis/health or https://myGHES.com/_services/vstoken/_apis/health using dotnet, check response headers contains `x-vss-e2eid`
---
- DNS lookup for pipelines.actions.githubusercontent.com using dotnet
- Ping pipelines.actions.githubusercontent.com using dotnet
- Make HTTP GET to https://pipelines.actions.githubusercontent.com/_apis/health or https://myGHES.com/_services/pipelines/_apis/health using dotnet, check response headers contains `x-vss-e2eid`
- Make HTTP POST to https://pipelines.actions.githubusercontent.com/_apis/health or https://myGHES.com/_services/pipelines/_apis/health using dotnet, check response headers contains `x-vss-e2eid`
---
- DNS lookup for results-receiver.actions.githubusercontent.com using dotnet
- Ping results-receiver.actions.githubusercontent.com using dotnet
- Make HTTP GET to https://results-receiver.actions.githubusercontent.com/health using dotnet, check response headers contains `X-GitHub-Request-Id`

## How to fix the issue?

### 1. Check the common network issue
  
  > Please check the [network doc](./network.md)

### 2. SSL certificate related issue

  If you are seeing `System.Net.Http.HttpRequestException: The SSL connection could not be established, see inner exception.` in the log, it means the runner can't connect to Actions service due to SSL handshake failure.
  > Please check the [SSL cert doc](./sslcert.md)
  
### 3. Authenticated action archive caches

If a gateway redirects action archive downloads to an HTTPS cache that requires Basic authentication, add an explicit entry for the cache hostname to the runner service account's `.netrc` file:

```text
machine internal.cache
  login builder
  password your-cache-password
```

The runner uses the file named by the `NETRC` environment variable, or otherwise `.netrc` in the service account's home directory, falling back to `_netrc`. Set `NETRC` in the runner service's environment before starting it. If the configured file is missing, or the selected file is unreadable or malformed, the runner adds a warning to the job log and continues without `.netrc` credentials. A valid file without an entry for a redirect host does not trigger a warning.

Machine names match hostnames without a scheme, path, or port, including when the cache uses a nonstandard HTTPS port. IPv6 literals include brackets, for example `machine [::1]`. `default` entries are ignored. Credentials are sent only over HTTPS, and the original GitHub authorization header is cleared on every redirect. These settings apply to redirected action repository archive downloads.

Double-quoted values follow curl 7.84.0 and later: `\"` and `\\` encode a quote and backslash, while `\n`, `\r`, and `\t` encode newline, carriage return, and tab. Older curl versions do not support quoted values, and Python's `netrc` parser and `git-credential-netrc` interpret escapes differently. Check compatibility with other tools when sharing a file that contains quoted or escaped credentials.

For an HTTP 401 from the cache, the download fails immediately and the job error distinguishes missing credentials from credentials that were sent and rejected, without requiring debug logging. Check the file location, the matching machine entry, and its login and password. HTTPS-to-HTTP redirects and chains longer than ten redirects also fail without retrying.

Each request has a 100-second timeout for receiving response headers. Each download attempt also has a 20-minute total timeout covering the redirect chain and streamed archive body; receiving headers does not restart this budget. Timeouts may trigger up to two retries. Cancelling the job also cancels an in-progress download.

## Still not working?

Contact [GitHub Support](https://support.github.com) if you have further questuons, or log an issue at https://github.com/actions/runner if you think it's a runner issue.
