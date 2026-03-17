package cmd

import (
	"strconv"

	"github.com/youngwoocho02/unity-cli/internal/client"
)

func consoleCmd(args []string, send sendFn) (*client.CommandResponse, error) {
	params := map[string]interface{}{}

	flags := parseSubFlags(args)

	if _, ok := flags["clear"]; ok {
		params["action"] = "clear"
		return send("read_console", params)
	}

	if v, ok := flags["lines"]; ok {
		if n, err := strconv.Atoi(v); err == nil {
			params["count"] = n
		}
	}
	if v, ok := flags["filter"]; ok {
		switch v {
		case "all":
			params["types"] = []string{"error", "warning", "log"}
		case "error":
			params["types"] = []string{"error"}
		case "warn", "warning":
			params["types"] = []string{"warning"}
		case "log":
			params["types"] = []string{"log"}
		default:
			params["types"] = []string{"error", "warning", "log"}
			params["filterText"] = v
		}
	}

	// --stacktrace: none (first line only), short (filter internal frames), full (raw)
	if v, ok := flags["stacktrace"]; ok {
		params["stacktrace"] = v
	}

	return send("read_console", params)
}
