# unity-cli

CLI tool to control Unity Editor from the command line.

## Structure

```
cmd/                  # Go CLI commands (manual dispatch, no cobra)
  root.go             # Entry point, flag parsing, sendFn injection
  *.go                # Commands: console, editor, exec, menu, profiler, reserialize, status, update
  *_test.go           # Unit tests in the same package
internal/client/      # Unity HTTP client, instance discovery
unity-connector/      # C# Unity Editor package (UPM)
  Editor/
    Core/             # Shared utilities (Response, ParamCoercion, StringCase)
    Tools/            # Tool implementations (auto-registered via [UnityCliTool] attribute)
```

## Development

### Adding a Command

1. Create `cmd/{name}.go`
2. Implement `{name}Cmd(args []string, send sendFn)`
3. Register in the dispatch switch in `cmd/root.go`
4. Add a corresponding C# tool in `unity-connector/Editor/Tools/` with `[UnityCliTool]`

### sendFn Pattern

All command functions receive a `sendFn` instead of calling Unity directly.
This decouples them from the HTTP layer so they can be unit tested with a mock.

```go
type sendFn func(command string, params interface{}) (*client.CommandResponse, error)
```

### Flag Parsing

- Global flags (`--port`, `--project`, `--timeout`): separated by `splitArgs()`, parsed by `flag.CommandLine`
- Subcommand flags (`--wait`, `--filter`, etc.): parsed by `parseSubFlags()`

## Verification

Run all of the following before pushing:

```bash
go clean -testcache
gofmt -w .
~/go/bin/golangci-lint run ./...
~/go/bin/golangci-lint fmt --diff
go test ./...
```

### Integration Tests (requires Unity)

Integration tests are tagged with `//go:build integration` and excluded from the default test run.
Run them manually when Unity Editor is open:

```bash
go test -tags integration ./...
```

CI skips these since Unity is not available.

## Git

Commit all unstaged changes before finishing. Unrelated changes should be committed separately.

## CI

- `push/PR → main`: build, vet, test, lint, format
- `tag push (v*)`: cross-compile (linux/darwin/windows × amd64/arm64) + GitHub Release
