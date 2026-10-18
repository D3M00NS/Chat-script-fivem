package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"reflect"
	"runtime"
	"strings"
	"testing"
	"time"
)

// Every identifier, player and reason below is invented test data.
const sampleDatabase = `{
  "version": 5,
  "players": [{"license":"license:invented-player", "unknownCounter":900719925474099312345}],
  "actions": [
    {"id":"B123-4567","type":"ban","ids":["license:invented-player"],"hwids":["invented-hwid"],"playerName":"Pessoa fictícia","reason":"Teste autorizado","author":"fixture-admin","timestamp":1700000000,"expiration":false,"revocation":{"timestamp":null,"author":null,"futureNote":{"keep":900719925474099312346}},"unknown":{"precise":9.00719925474099312347e22}},
    {"id":"W123-4567","type":"warn","ids":["license:other-invented-player"],"playerName":false,"reason":"Aviso fictício","author":"fixture-admin","timestamp":1700000001,"expiration":false,"acked":false,"revocation":{"timestamp":null,"author":null}}
  ],
  "whitelistApprovals": [{"unknown":true}],
  "whitelistRequests": [],
  "futureRoot": {"text":"<preserve me>","counter":900719925474099312348}
}`

func fixture(t *testing.T, content string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "playersDB.json")
	if err := os.WriteFile(path, []byte(content), 0640); err != nil {
		t.Fatal(err)
	}
	if err := os.Chmod(path, 0640); err != nil {
		t.Fatal(err)
	}
	return path
}

func readFile(t *testing.T, path string) []byte {
	t.Helper()
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	return data
}

func decodeExact(t *testing.T, data []byte) map[string]any {
	t.Helper()
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	var value map[string]any
	if err := decoder.Decode(&value); err != nil {
		t.Fatal(err)
	}
	return value
}

func assertUnchanged(t *testing.T, path, content string) {
	t.Helper()
	if string(readFile(t, path)) != content {
		t.Fatal("failed or read-only operation changed original database")
	}
	entries, err := os.ReadDir(filepath.Dir(path))
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 1 {
		t.Fatalf("failed or read-only operation left unexpected files: %v", entries)
	}
}

func TestInspectReadOnlyAndNormalizedID(t *testing.T) {
	path := fixture(t, sampleDatabase)
	info, err := inspectBan(path, " \tb123-4567\n")
	if err != nil {
		t.Fatal(err)
	}
	if info.ID != "B123-4567" || info.Reason != "Teste autorizado" || info.Target != "Pessoa fictícia (license:invented-player)" {
		t.Fatalf("unexpected confirmation: %+v", info)
	}
	if info.DatabaseSHA256 != databaseHash([]byte(sampleDatabase)) {
		t.Fatal("confirmation does not identify exact source bytes")
	}
	assertUnchanged(t, path, sampleDatabase)
}

func TestRevokeOnlyChangesSelectedRevocationAndBacksUpExactBytes(t *testing.T) {
	path := fixture(t, sampleDatabase)
	info, err := inspectBan(path, "B123-4567")
	if err != nil {
		t.Fatal(err)
	}
	before := time.Now().Unix()
	backup, err := revokeBan(path, " b123-4567 ", info.DatabaseSHA256)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.HasPrefix(filepath.Base(backup), "playersDB.json.local-unban-backup-") || filepath.Dir(backup) != filepath.Dir(path) {
		t.Fatalf("backup outside original folder or wrong name: %s", backup)
	}
	if string(readFile(t, backup)) != sampleDatabase {
		t.Fatal("backup differs from original bytes")
	}
	original := decodeExact(t, []byte(sampleDatabase))
	updated := decodeExact(t, readFile(t, path))
	originalBan := original["actions"].([]any)[0].(map[string]any)
	updatedBan := updated["actions"].([]any)[0].(map[string]any)
	revocation := updatedBan["revocation"].(map[string]any)
	stamp, err := revocation["timestamp"].(json.Number).Int64()
	if err != nil || stamp < before || stamp > time.Now().Unix() || revocation["author"] != "local-admin-tool" {
		t.Fatalf("unexpected audit fields: %+v", revocation)
	}
	wantedRevocation := originalBan["revocation"].(map[string]any)
	wantedRevocation["timestamp"] = revocation["timestamp"]
	wantedRevocation["author"] = "local-admin-tool"
	if !reflect.DeepEqual(original, updated) {
		t.Fatal("revocation changed another action, record, unknown field, or precise number")
	}
	if runtime.GOOS != "windows" {
		for _, filename := range []string{path, backup} {
			stat, err := os.Stat(filename)
			if err != nil || stat.Mode().Perm() != 0640 {
				t.Fatalf("permissions not preserved on %s: %v %v", filename, stat, err)
			}
		}
	}
	entries, _ := os.ReadDir(filepath.Dir(path))
	if len(entries) != 2 {
		t.Fatalf("staging file not cleaned up: %v", entries)
	}
	if _, err := inspectBan(path, "B123-4567"); err == nil {
		t.Fatal("revoked ban must be treated as no-op")
	}
}

func TestStaleOrMissingConfirmationNeverWrites(t *testing.T) {
	for _, hash := range []string{"", strings.Repeat("0", 64), strings.Repeat("X", 64)} {
		t.Run(hash, func(t *testing.T) {
			path := fixture(t, sampleDatabase)
			if backup, err := revokeBan(path, "B123-4567", hash); err == nil || backup != "" {
				t.Fatal("invalid confirmation permitted a write")
			}
			assertUnchanged(t, path, sampleDatabase)
		})
	}
	path := fixture(t, sampleDatabase)
	info, _ := inspectBan(path, "B123-4567")
	changed := sampleDatabase + "\n"
	if err := os.WriteFile(path, []byte(changed), 0640); err != nil {
		t.Fatal(err)
	}
	if _, err := revokeBan(path, "B123-4567", info.DatabaseSHA256); err == nil {
		t.Fatal("stale confirmation accepted")
	}
	assertUnchanged(t, path, changed)
}

func TestRejectInvalidSchemaAndInactiveBansWithoutWriting(t *testing.T) {
	cases := map[string]string{
		"root array":             `[]`,
		"old version":            strings.Replace(sampleDatabase, `"version": 5`, `"version": 4`, 1),
		"string version":         strings.Replace(sampleDatabase, `"version": 5`, `"version": "5"`, 1),
		"missing array":          strings.Replace(sampleDatabase, `"whitelistRequests": []`, `"otherList": []`, 1),
		"null array":             strings.Replace(sampleDatabase, `"whitelistRequests": []`, `"whitelistRequests": null`, 1),
		"null action":            strings.Replace(sampleDatabase, `"actions": [`, `"actions": [null,`, 1),
		"selected warn":          strings.Replace(sampleDatabase, `"type":"ban"`, `"type":"warn"`, 1),
		"duplicate ID":           strings.Replace(sampleDatabase, `"id":"W123-4567"`, `"id":"B123-4567"`, 1),
		"missing IDs":            strings.Replace(sampleDatabase, `"ids":["license:invented-player"]`, `"ids":[]`, 1),
		"null identifier":        strings.Replace(sampleDatabase, `"ids":["license:invented-player"]`, `"ids":[null]`, 1),
		"wrong hwids":            strings.Replace(sampleDatabase, `"hwids":["invented-hwid"]`, `"hwids":null`, 1),
		"missing timestamp":      strings.Replace(sampleDatabase, `"timestamp":1700000000,`, ``, 1),
		"null reason":            strings.Replace(sampleDatabase, `"reason":"Teste autorizado"`, `"reason":null`, 1),
		"false reason":           strings.Replace(sampleDatabase, `"reason":"Teste autorizado"`, `"reason":false`, 1),
		"null revocation":        strings.Replace(sampleDatabase, `"revocation":{"timestamp":null,"author":null,"futureNote":{"keep":900719925474099312346}}`, `"revocation":null`, 1),
		"revocation missing key": strings.Replace(sampleDatabase, `"timestamp":null,"author":null`, `"timestamp":null`, 1),
		"revoked":                strings.Replace(sampleDatabase, `"timestamp":null,"author":null`, `"timestamp":1700000002,"author":"other-admin"`, 1),
		"inconsistent revoked":   strings.Replace(sampleDatabase, `"timestamp":null,"author":null`, `"timestamp":null,"author":"other-admin"`, 1),
		"expired":                strings.Replace(sampleDatabase, `"expiration":false`, `"expiration":1700000002`, 1),
		"true expiry":            strings.Replace(sampleDatabase, `"expiration":false`, `"expiration":true`, 1),
		"null expiry":            strings.Replace(sampleDatabase, `"expiration":false`, `"expiration":null`, 1),
		"massive exponent":       strings.Replace(sampleDatabase, `"expiration":false`, `"expiration":1e99999999`, 1),
		"malformed":              sampleDatabase[:len(sampleDatabase)-1],
		"trailing content":       sampleDatabase + ` {}`,
		"invalid UTF8":           strings.Replace(sampleDatabase, "preserve me", string([]byte{0xff}), 1),
	}
	for name, content := range cases {
		t.Run(name, func(t *testing.T) {
			path := fixture(t, content)
			if _, err := inspectBan(path, "B123-4567"); err == nil {
				t.Fatal("invalid database or inactive ban accepted")
			}
			if backup, err := revokeBan(path, "B123-4567", databaseHash([]byte(content))); err == nil || backup != "" {
				t.Fatal("invalid database or inactive ban was modified")
			}
			assertUnchanged(t, path, content)
		})
	}
}

func TestDuplicateKeysAreRejectedEverywhere(t *testing.T) {
	cases := map[string]string{
		"root":        strings.Replace(sampleDatabase, `"version": 5`, `"version": 5, "version": 5`, 1),
		"action":      strings.Replace(sampleDatabase, `"type":"ban"`, `"type":"ban","type":"ban"`, 1),
		"revocation":  strings.Replace(sampleDatabase, `"timestamp":null,"author":null`, `"timestamp":null,"timestamp":null,"author":null`, 1),
		"unknown":     strings.Replace(sampleDatabase, `"futureRoot": {`, `"futureRoot": {"ambiguous":1,"ambiguous":2,`, 1),
		"escaped key": strings.Replace(sampleDatabase, `"version": 5`, `"version": 5, "\u0076ersion": 5`, 1),
	}
	for name, content := range cases {
		t.Run(name, func(t *testing.T) {
			path := fixture(t, content)
			if _, err := inspectBan(path, "B123-4567"); err == nil {
				t.Fatal("ambiguous JSON accepted")
			}
			if _, err := revokeBan(path, "B123-4567", databaseHash([]byte(content))); err == nil {
				t.Fatal("ambiguous JSON was modified")
			}
			assertUnchanged(t, path, content)
		})
	}
}

func TestWrongIDAndMissingBanNeverWrites(t *testing.T) {
	for _, id := range []string{"", "*", "B000-0000", "BIII-OOOO", "B1234567", "B123-45678", "B234-5678", "W123-4567"} {
		t.Run(id, func(t *testing.T) {
			path := fixture(t, sampleDatabase)
			if _, err := inspectBan(path, id); err == nil {
				t.Fatal("wrong or missing ID accepted")
			}
			if _, err := revokeBan(path, id, databaseHash([]byte(sampleDatabase))); err == nil {
				t.Fatal("wrong or missing ID caused a write")
			}
			assertUnchanged(t, path, sampleDatabase)
		})
	}
}

func TestFalsePlayerNameAndFutureExpiration(t *testing.T) {
	content := strings.Replace(sampleDatabase, `"playerName":"Pessoa fictícia"`, `"playerName":false`, 1)
	content = strings.Replace(content, `"expiration":false`, fmt.Sprintf(`"expiration":%d`, time.Now().Unix()+3600), 1)
	path := fixture(t, content)
	info, err := inspectBan(path, "B123-4567")
	if err != nil || info.Target != "license:invented-player" {
		t.Fatalf("false playerName or future expiration rejected: %+v %v", info, err)
	}
	if _, err := revokeBan(path, info.ID, info.DatabaseSHA256); err != nil {
		t.Fatal(err)
	}
}

func TestBackupsNeverClobberExistingFiles(t *testing.T) {
	path := fixture(t, sampleDatabase)
	existing := path + ".local-unban-backup-existing.json"
	if err := os.WriteFile(existing, []byte("existing user backup"), 0600); err != nil {
		t.Fatal(err)
	}
	var previous string
	for i := 0; i < 2; i++ {
		if i > 0 {
			if err := os.WriteFile(path, []byte(sampleDatabase), 0640); err != nil {
				t.Fatal(err)
			}
		}
		backup, err := revokeBan(path, "B123-4567", databaseHash([]byte(sampleDatabase)))
		if err != nil || backup == previous || backup == existing {
			t.Fatalf("backup must be unique: %s %v", backup, err)
		}
		if string(readFile(t, backup)) != sampleDatabase {
			t.Fatal("new backup differs from original")
		}
		previous = backup
	}
	if string(readFile(t, existing)) != "existing user backup" {
		t.Fatal("existing backup overwritten")
	}
}

func TestExistingOperationLockPreservesDatabase(t *testing.T) {
	path := fixture(t, sampleDatabase)
	lockPath := path + ".local-unban.lock"
	if err := os.WriteFile(lockPath, []byte("existing operation"), 0600); err != nil {
		t.Fatal(err)
	}
	if backup, err := revokeBan(path, "B123-4567", databaseHash([]byte(sampleDatabase))); err == nil || backup != "" {
		t.Fatal("concurrent operation lock was ignored")
	}
	if string(readFile(t, path)) != sampleDatabase || string(readFile(t, lockPath)) != "existing operation" {
		t.Fatal("existing database or lock modified")
	}
	entries, _ := os.ReadDir(filepath.Dir(path))
	if len(entries) != 2 {
		t.Fatal("blocked operation created extra files")
	}
}

func TestSymlinkFileAndAncestorRejected(t *testing.T) {
	root := t.TempDir()
	realDir := filepath.Join(root, "real")
	if err := os.Mkdir(realDir, 0700); err != nil {
		t.Fatal(err)
	}
	path := filepath.Join(realDir, "playersDB.json")
	if err := os.WriteFile(path, []byte(sampleDatabase), 0600); err != nil {
		t.Fatal(err)
	}
	fileLink := filepath.Join(root, "file-link.json")
	dirLink := filepath.Join(root, "dir-link")
	if err := os.Symlink(path, fileLink); err != nil {
		t.Skipf("cannot create symlink on this platform: %v", err)
	}
	if err := os.Symlink(realDir, dirLink); err != nil {
		t.Fatal(err)
	}
	for _, selectedPath := range []string{fileLink, filepath.Join(dirLink, "playersDB.json")} {
		if _, err := inspectBan(selectedPath, "B123-4567"); err == nil {
			t.Fatalf("symlink accepted: %s", selectedPath)
		}
		if _, err := revokeBan(selectedPath, "B123-4567", databaseHash([]byte(sampleDatabase))); err == nil {
			t.Fatal("symlink used for mutation")
		}
	}
	assertUnchanged(t, path, sampleDatabase)
}

func TestOversizeAndNonregularFilesRejected(t *testing.T) {
	path := filepath.Join(t.TempDir(), "large.json")
	file, err := os.Create(path)
	if err != nil {
		t.Fatal(err)
	}
	if err := file.Truncate(maxDatabaseBytes + 1); err != nil {
		file.Close()
		t.Fatal(err)
	}
	file.Close()
	for _, selectedPath := range []string{path, filepath.Dir(path), filepath.Join(filepath.Dir(path), "missing.json"), ""} {
		if _, err := inspectBan(selectedPath, "B123-4567"); err == nil {
			t.Fatalf("unusable file accepted: %s", selectedPath)
		}
	}
}

func TestWriteFailurePreservesDatabase(t *testing.T) {
	if runtime.GOOS == "windows" {
		t.Skip("POSIX directory permission test")
	}
	path := fixture(t, sampleDatabase)
	dir := filepath.Dir(path)
	if err := os.Chmod(dir, 0500); err != nil {
		t.Fatal(err)
	}
	defer os.Chmod(dir, 0700)
	probe, err := os.CreateTemp(dir, "permission-probe-*")
	if err == nil {
		name := probe.Name()
		probe.Close()
		os.Remove(name)
		t.Skip("current account bypasses directory permissions")
	}
	if backup, err := revokeBan(path, "B123-4567", databaseHash([]byte(sampleDatabase))); err == nil || backup != "" {
		t.Fatal("write failure was not reported")
	}
	assertUnchanged(t, path, sampleDatabase)
}

func TestUnsupportedJSONDepthRejected(t *testing.T) {
	content := strings.Replace(sampleDatabase, `"whitelistRequests": []`, `"whitelistRequests": `+strings.Repeat("[", 260)+strings.Repeat("]", 260), 1)
	path := fixture(t, content)
	if _, err := inspectBan(path, "B123-4567"); err == nil {
		t.Fatal("excessively nested JSON accepted")
	}
	assertUnchanged(t, path, content)
}
