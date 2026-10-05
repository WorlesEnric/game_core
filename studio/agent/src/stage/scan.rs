//! Stage step (a): the forbidden-content scan of a candidate mechanism package (03 §8;
//! `studio/stage/forbidden-rules.md` documents every rule).
//!
//! C# sources are lexed (comments dropped, string literals kept as their own tokens, line
//! numbers kept, `#if UNITY_EDITOR` regions tracked) and the rules match token sequences, so a
//! comment that mentions `Process.Start` is not a hit and `Process . Start` split over spaces
//! is. Assembly definitions, every text file (credential patterns) and every file's size
//! (unmanifested binary blobs) are checked too. A hit is `{rule, path, line, excerpt}`; the
//! excerpt is redacted and at most 160 characters.

use std::collections::BTreeSet;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::redact::redact;

/// Blobs larger than this need a manifest entry (`proposal.blobs` / `package.json` `gamecore.blobs`).
pub const MAX_UNMANIFESTED_BLOB: u64 = 2 * 1024 * 1024;

/// Every rule id, in report order.
pub const RULES: &[&str] = &[
    "reflection-emit",
    "process-start",
    "file-write",
    "file-access",
    "editor-hook",
    "editor-in-runtime",
    "dllimport",
    "native-plugin",
    "unsafe",
    "network",
    "resources-absolute",
    "static-mutable",
    "credentials",
    "binary-blob",
];

/// One forbidden-content finding.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Hit {
    /// Rule id (one of [`RULES`]).
    pub rule: String,
    /// Path relative to the package root.
    pub path: String,
    /// 1-based line (0 for whole-file findings).
    pub line: usize,
    /// The offending line, trimmed, redacted, at most 160 characters.
    pub excerpt: String,
}

/// What the scan needs to know about the package.
#[derive(Debug, Clone, Default)]
pub struct ScanContext {
    /// The package name used by diagnostics; it never grants filesystem authority.
    pub package: String,
    /// Legacy proposal reason retained for decoding only; unsafe code is always refused.
    pub allow_unsafe: Option<String>,
    /// Paths (relative to the package root) of large binaries the package declares.
    pub blobs: BTreeSet<String>,
    /// Documented allowlist entries (`studio/stage/allowlist.json` `scanExemptions`).
    pub exemptions: Vec<Exemption>,
}

/// One documented scan exemption: a hit of `rule` is not forbidden when the file's path ends
/// with `path_suffix` inside a directory named `directory`, the file's first lines contain
/// `header`, and the offending line contains `excerpt_contains`. Every field must match; an
/// exempted hit is still reported (step facts and log), never silently dropped.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct Exemption {
    /// Stable id, quoted in the step log and facts.
    pub id: String,
    /// The rule it applies to (one of [`RULES`]).
    pub rule: String,
    /// The file name suffix (e.g. `.g.cs`).
    pub path_suffix: String,
    /// A directory name the file must sit directly in (e.g. `Generated`).
    pub directory: String,
    /// A line the first ten lines of the file must contain (the generator's header).
    pub header: String,
    /// Text the offending line must contain (e.g. `static readonly`).
    pub excerpt_contains: String,
    /// Why the exemption is legitimate (documentation; required non-empty).
    pub reason: String,
}

impl Exemption {
    fn matches(&self, hit: &Hit, head: &str, line: &str) -> bool {
        let parent = Path::new(&hit.path)
            .parent()
            .and_then(|p| p.file_name())
            .map(|n| n.to_string_lossy().to_string());
        hit.rule == self.rule
            && !self.reason.trim().is_empty()
            && hit.path.ends_with(&self.path_suffix)
            && parent.as_deref() == Some(self.directory.as_str())
            && head
                .lines()
                .take(10)
                .any(|l| l.trim() == self.header.trim())
            && line.contains(&self.excerpt_contains)
    }
}

/// Reads `scanExemptions` from a stage allowlist (`studio/stage/allowlist.json`). Entries
/// naming an unknown rule or without a reason are refused.
pub fn load_exemptions(allowlist: &Path) -> Result<Vec<Exemption>, String> {
    let text = std::fs::read_to_string(allowlist)
        .map_err(|e| format!("cannot read {}: {e}", allowlist.display()))?;
    let doc: Value = serde_json::from_str(&text)
        .map_err(|e| format!("{} is not JSON: {e}", allowlist.display()))?;
    let Some(list) = doc.get("scanExemptions") else {
        return Ok(Vec::new());
    };
    let out: Vec<Exemption> = serde_json::from_value(list.clone())
        .map_err(|e| format!("{}: scanExemptions: {e}", allowlist.display()))?;
    for x in &out {
        if !RULES.contains(&x.rule.as_str()) {
            return Err(format!(
                "scan exemption {} names unknown rule {}",
                x.id, x.rule
            ));
        }
        if x.reason.trim().is_empty() || x.header.trim().is_empty() {
            return Err(format!(
                "scan exemption {} needs a reason and a header",
                x.id
            ));
        }
    }
    Ok(out)
}

/// The scan of one package.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScanReport {
    /// Files inspected.
    pub files: usize,
    /// Bytes inspected.
    pub bytes: u64,
    /// Findings, in path then line order.
    pub hits: Vec<Hit>,
    /// Findings an [`Exemption`] covers, with the exemption id.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub exempted: Vec<(String, Hit)>,
}

// ---------------------------------------------------------------------------------------------
// Lexing.

#[derive(Debug, Clone, PartialEq)]
enum Tok {
    Ident(String),
    Str(String),
    Punct(char),
    Number,
}

#[derive(Debug, Clone)]
struct Token {
    tok: Tok,
    line: usize,
}

/// A lexed C# file: tokens, and the lines inside a positive `#if UNITY_EDITOR` branch.
struct Lexed {
    tokens: Vec<Token>,
    editor_lines: BTreeSet<usize>,
}

fn is_ident_start(c: char) -> bool {
    c == '_' || c.is_alphabetic()
}

fn is_ident_char(c: char) -> bool {
    c == '_' || c.is_alphanumeric()
}

/// Positive `UNITY_EDITOR` guard of a preprocessor line (`#if UNITY_EDITOR`, `#if A && UNITY_EDITOR`).
fn editor_positive(condition: &str) -> bool {
    let compact: String = condition.chars().filter(|c| !c.is_whitespace()).collect();
    compact.contains("UNITY_EDITOR")
        && !compact.contains("!UNITY_EDITOR")
        && !compact.contains("||")
}

fn lex(text: &str) -> Lexed {
    let chars: Vec<char> = text.chars().collect();
    let mut tokens = Vec::new();
    let mut editor_lines = BTreeSet::new();
    let mut guard: Vec<bool> = Vec::new();
    let mut i = 0;
    let mut line = 1;
    let mut at_line_start = true;
    while i < chars.len() {
        let c = chars[i];
        if c == '\n' {
            line += 1;
            i += 1;
            at_line_start = true;
            continue;
        }
        if c.is_whitespace() {
            i += 1;
            continue;
        }
        if at_line_start && c == '#' {
            // A preprocessor directive: the rest of the line.
            let start = i + 1;
            while i < chars.len() && chars[i] != '\n' {
                i += 1;
            }
            let directive: String = chars[start..i].iter().collect();
            let directive = directive.trim();
            let (keyword, condition) = directive
                .split_once(char::is_whitespace)
                .unwrap_or((directive, ""));
            match keyword {
                "if" => guard.push(editor_positive(condition)),
                "elif" | "else" => {
                    if let Some(last) = guard.last_mut() {
                        *last = false;
                    }
                }
                "endif" => {
                    guard.pop();
                }
                _ => {}
            }
            continue;
        }
        at_line_start = false;
        if guard.iter().any(|g| *g) {
            editor_lines.insert(line);
        }
        // Comments.
        if c == '/' && chars.get(i + 1) == Some(&'/') {
            while i < chars.len() && chars[i] != '\n' {
                i += 1;
            }
            continue;
        }
        if c == '/' && chars.get(i + 1) == Some(&'*') {
            i += 2;
            while i < chars.len() && !(chars[i] == '*' && chars.get(i + 1) == Some(&'/')) {
                if chars[i] == '\n' {
                    line += 1;
                }
                i += 1;
            }
            i = (i + 2).min(chars.len());
            continue;
        }
        // String literals: "..", @"..", $"..", $@"..", @$"..".
        let (verbatim, quote_at) = match (c, chars.get(i + 1), chars.get(i + 2)) {
            ('"', _, _) => (false, Some(i)),
            ('@', Some('"'), _) => (true, Some(i + 1)),
            ('$', Some('"'), _) => (false, Some(i + 1)),
            ('$', Some('@'), Some('"')) | ('@', Some('$'), Some('"')) => (true, Some(i + 2)),
            _ => (false, None),
        };
        if let Some(q) = quote_at {
            let start_line = line;
            let mut j = q + 1;
            let mut content = String::new();
            while j < chars.len() {
                let d = chars[j];
                if verbatim {
                    if d == '"' {
                        if chars.get(j + 1) == Some(&'"') {
                            content.push('"');
                            j += 2;
                            continue;
                        }
                        break;
                    }
                } else {
                    if d == '\\' {
                        if let Some(e) = chars.get(j + 1) {
                            content.push(*e);
                        }
                        j += 2;
                        continue;
                    }
                    if d == '"' || d == '\n' {
                        break;
                    }
                }
                if d == '\n' {
                    line += 1;
                }
                content.push(d);
                j += 1;
            }
            tokens.push(Token {
                tok: Tok::Str(content),
                line: start_line,
            });
            i = (j + 1).min(chars.len());
            continue;
        }
        if c == '\'' {
            let mut j = i + 1;
            while j < chars.len() && chars[j] != '\'' && chars[j] != '\n' {
                if chars[j] == '\\' {
                    j += 1;
                }
                j += 1;
            }
            tokens.push(Token {
                tok: Tok::Str(String::new()),
                line,
            });
            i = (j + 1).min(chars.len());
            continue;
        }
        if is_ident_start(c) || (c == '@' && chars.get(i + 1).is_some_and(|n| is_ident_start(*n))) {
            let start = if c == '@' { i + 1 } else { i };
            let mut j = start;
            while j < chars.len() && is_ident_char(chars[j]) {
                j += 1;
            }
            tokens.push(Token {
                tok: Tok::Ident(chars[start..j].iter().collect()),
                line,
            });
            i = j;
            continue;
        }
        if c.is_ascii_digit() {
            let mut j = i;
            while j < chars.len()
                && (chars[j].is_ascii_alphanumeric() || chars[j] == '.' || chars[j] == '_')
            {
                j += 1;
            }
            tokens.push(Token {
                tok: Tok::Number,
                line,
            });
            i = j;
            continue;
        }
        tokens.push(Token {
            tok: Tok::Punct(c),
            line,
        });
        i += 1;
    }
    Lexed {
        tokens,
        editor_lines,
    }
}

// ---------------------------------------------------------------------------------------------
// Rules over tokens.

fn ident(t: Option<&Token>) -> Option<&str> {
    match t.map(|t| &t.tok) {
        Some(Tok::Ident(s)) => Some(s.as_str()),
        _ => None,
    }
}

fn punct(t: Option<&Token>, c: char) -> bool {
    matches!(t.map(|t| &t.tok), Some(Tok::Punct(p)) if *p == c)
}

/// True when the tokens at `i..` spell the dotted path `parts` (e.g. `System . Net`).
fn path_at(tokens: &[Token], i: usize, parts: &[&str]) -> bool {
    let mut k = i;
    for (n, part) in parts.iter().enumerate() {
        if n > 0 {
            if !punct(tokens.get(k), '.') {
                return false;
            }
            k += 1;
        }
        if ident(tokens.get(k)) != Some(part) {
            return false;
        }
        k += 1;
    }
    true
}

/// The first string literal of the statement starting at `i` (up to `;` or `{`).
fn statement_string(tokens: &[Token], i: usize) -> Option<&str> {
    for t in tokens.iter().skip(i).take(64) {
        match &t.tok {
            Tok::Str(s) => return Some(s.as_str()),
            Tok::Punct(';') | Tok::Punct('{') | Tok::Punct('}') => return None,
            _ => {}
        }
    }
    None
}

const FILE_WRITES: &[&str] = &[
    "WriteAllText",
    "WriteAllBytes",
    "WriteAllLines",
    "WriteAllTextAsync",
    "WriteAllBytesAsync",
    "WriteAllLinesAsync",
    "AppendAllText",
    "AppendAllLines",
    "AppendAllTextAsync",
    "AppendAllLinesAsync",
    "AppendText",
    "Create",
    "CreateText",
    "CreateSymbolicLink",
    "Delete",
    "Move",
    "Copy",
    "Replace",
    "Open",
    "OpenWrite",
    "SetAttributes",
    "SetCreationTime",
    "SetLastWriteTime",
    "Encrypt",
    "Decrypt",
];

const DIRECTORY_WRITES: &[&str] = &["CreateDirectory", "Delete", "Move", "CreateSymbolicLink"];

const WRITER_TYPES: &[&str] = &["FileStream", "StreamWriter", "BinaryWriter"];

const NETWORK_TYPES: &[&str] = &[
    "UnityWebRequest",
    "HttpClient",
    "HttpClientHandler",
    "WebClient",
    "WebRequest",
    "HttpWebRequest",
    "TcpClient",
    "TcpListener",
    "UdpClient",
    "Socket",
    "WebSocket",
    "ClientWebSocket",
    "Dns",
    "NetworkStream",
];

const EMIT_TYPES: &[&str] = &[
    "ILGenerator",
    "DynamicMethod",
    "AssemblyBuilder",
    "ModuleBuilder",
    "TypeBuilder",
    "MethodBuilder",
];

const MUTABLE_COLLECTIONS: &[&str] = &[
    "List",
    "Dictionary",
    "HashSet",
    "SortedSet",
    "Queue",
    "Stack",
    "SortedDictionary",
    "SortedList",
    "LinkedList",
    "ConcurrentDictionary",
    "ConcurrentQueue",
    "ConcurrentBag",
    "ConcurrentStack",
    "StringBuilder",
    "ArrayList",
    "Hashtable",
];

const MODIFIERS: &[&str] = &[
    "public",
    "private",
    "protected",
    "internal",
    "new",
    "volatile",
    "unsafe",
    "readonly",
    "const",
    "extern",
    "partial",
    "abstract",
    "sealed",
    "override",
    "virtual",
    "async",
    "required",
];

const NOT_A_FIELD: &[&str] = &[
    "class",
    "struct",
    "interface",
    "enum",
    "delegate",
    "void",
    "extern",
    "operator",
    "implicit",
    "explicit",
    "partial",
    "record",
    "abstract",
];

/// The kind of a `static` declaration at `i` (the `static` token): `None` when it is not a
/// mutable static field, else a short description.
fn static_field(tokens: &[Token], i: usize) -> Option<&'static str> {
    // `using static X;` is a directive.
    if ident(i.checked_sub(1).and_then(|p| tokens.get(p))) == Some("using") {
        return None;
    }
    // Modifiers before `static` (attributes are skipped).
    let mut readonly = false;
    let mut k = i;
    while k > 0 {
        let prev = &tokens[k - 1];
        match &prev.tok {
            Tok::Ident(s) if MODIFIERS.contains(&s.as_str()) => {
                readonly |= s == "readonly" || s == "const";
                k -= 1;
            }
            Tok::Punct(']') => {
                // Skip an attribute block back to its '['.
                let mut depth = 0;
                while k > 0 {
                    k -= 1;
                    match &tokens[k].tok {
                        Tok::Punct(']') => depth += 1,
                        Tok::Punct('[') => {
                            depth -= 1;
                            if depth == 0 {
                                break;
                            }
                        }
                        _ => {}
                    }
                }
            }
            _ => break,
        }
    }
    // Tokens after `static` up to the terminator.
    let mut j = i + 1;
    let mut type_name: Option<&str> = None;
    let mut array = false;
    let mut depth_angle = 0i32;
    while j < tokens.len() && j < i + 48 {
        match &tokens[j].tok {
            Tok::Ident(s) => {
                let s = s.as_str();
                if s == "event" {
                    return Some("static event (a mutable static delegate field)");
                }
                if NOT_A_FIELD.contains(&s) && depth_angle == 0 {
                    return None;
                }
                if MODIFIERS.contains(&s) {
                    readonly |= s == "readonly" || s == "const";
                } else if type_name.is_none() {
                    type_name = Some(s);
                }
            }
            Tok::Punct('<') => depth_angle += 1,
            Tok::Punct('>') => depth_angle -= 1,
            Tok::Punct('[') if depth_angle == 0 => array = true,
            Tok::Punct('(') | Tok::Punct('{') => return None,
            Tok::Punct('=') => {
                if punct(tokens.get(j + 1), '>') {
                    return None;
                }
                break;
            }
            Tok::Punct(';') => break,
            _ => {}
        }
        j += 1;
    }
    if j >= tokens.len() || type_name.is_none() {
        return None;
    }
    if !readonly {
        return Some("a non-readonly static field");
    }
    if array {
        return Some("a static readonly array (its elements are mutable)");
    }
    if type_name.is_some_and(|t| MUTABLE_COLLECTIONS.contains(&t)) {
        return Some("a static readonly mutable collection");
    }
    None
}

fn excerpt_of(lines: &[&str], line: usize) -> String {
    let text = lines
        .get(line.saturating_sub(1))
        .copied()
        .unwrap_or("")
        .trim();
    let text = mask_secrets(&redact(text));
    if text.chars().count() > 160 {
        let cut: String = text.chars().take(157).collect();
        format!("{cut}...")
    } else {
        text
    }
}

/// Compatibility entry point: all masking uses the shared companion redactor.
pub fn mask_secrets(text: &str) -> String {
    redact(text)
}

/// Scans one C# source.
pub fn scan_csharp(rel: &str, text: &str, editor_assembly: bool, _ctx: &ScanContext) -> Vec<Hit> {
    let lexed = lex(text);
    let tokens = &lexed.tokens;
    let lines: Vec<&str> = text.lines().collect();
    let mut hits: Vec<Hit> = Vec::new();
    let mut add = |rule: &str, line: usize| {
        if !hits.iter().any(|h| h.rule == rule && h.line == line) {
            hits.push(Hit {
                rule: rule.to_string(),
                path: rel.to_string(),
                line,
                excerpt: excerpt_of(&lines, line),
            });
        }
    };
    let in_editor_folder = rel.split('/').rev().skip(1).any(|p| p == "Editor");
    for (i, t) in tokens.iter().enumerate() {
        let line = t.line;
        let Tok::Ident(name) = &t.tok else { continue };
        let name = name.as_str();
        // reflection-emit
        if (name == "System" && path_at(tokens, i, &["System", "Reflection", "Emit"]))
            || EMIT_TYPES.contains(&name)
        {
            add("reflection-emit", line);
        }
        // process-start
        if (name == "Process" && path_at(tokens, i, &["Process", "Start"]))
            || name == "ProcessStartInfo"
            || (name == "System" && path_at(tokens, i, &["System", "Diagnostics", "Process"]))
        {
            add("process-start", line);
        }
        // file-write
        let file_call = (name == "File"
            && punct(tokens.get(i + 1), '.')
            && ident(tokens.get(i + 2)).is_some_and(|m| FILE_WRITES.contains(&m)))
            || (name == "Directory"
                && punct(tokens.get(i + 1), '.')
                && ident(tokens.get(i + 2)).is_some_and(|m| DIRECTORY_WRITES.contains(&m)))
            || (WRITER_TYPES.contains(&name)
                && ident(i.checked_sub(1).and_then(|p| tokens.get(p))) == Some("new"));
        if file_call {
            add("file-write", line);
        }
        if [
            "InitializeOnLoad",
            "InitializeOnLoadMethod",
            "AssetPostprocessor",
            "AssetModificationProcessor",
            "DidReloadScripts",
            "MenuItem",
        ]
        .contains(&name)
        {
            add("editor-hook", line);
        }
        if ["File", "Directory", "FileStream", "StreamReader"].contains(&name) {
            add("file-access", line);
        }

        // editor-in-runtime
        if name == "UnityEditor"
            && !editor_assembly
            && !in_editor_folder
            && !lexed.editor_lines.contains(&line)
        {
            add("editor-in-runtime", line);
        }
        // dllimport
        if name == "DllImport" || name == "extern" || name == "LibraryImport" {
            add("dllimport", line);
        }
        // unsafe
        if name == "unsafe"
            || name == "stackalloc"
            || (name == "fixed" && punct(tokens.get(i + 1), '('))
        {
            add("unsafe", line);
        }
        // network
        if (name == "System" && path_at(tokens, i, &["System", "Net"]))
            || (name == "UnityEngine" && path_at(tokens, i, &["UnityEngine", "Networking"]))
            || NETWORK_TYPES.contains(&name)
        {
            add("network", line);
        }
        // resources-absolute
        if name == "Resources"
            && punct(tokens.get(i + 1), '.')
            && ident(tokens.get(i + 2)).is_some_and(|m| m.starts_with("Load"))
            && statement_string(tokens, i).is_some_and(|s| {
                s.starts_with('/') || s.starts_with('\\') || s.contains(':') || s.contains("..")
            })
        {
            add("resources-absolute", line);
        }
        // static-mutable
        if name == "ThreadStatic" {
            add("static-mutable", line);
        }
        if name == "static" && static_field(tokens, i).is_some() {
            add("static-mutable", line);
        }
    }
    hits
}

/// Scans an assembly definition (`allowUnsafeCode`, Editor references from runtime assemblies).
pub fn scan_asmdef(rel: &str, text: &str, _ctx: &ScanContext) -> Vec<Hit> {
    let mut hits = Vec::new();
    let Ok(doc) = serde_json::from_str::<Value>(text) else {
        return hits;
    };
    let editor_only = is_editor_only(&doc);
    if doc.get("allowUnsafeCode").and_then(Value::as_bool) == Some(true) {
        hits.push(Hit {
            rule: "unsafe".into(),
            path: rel.into(),
            line: 0,
            excerpt: "allowUnsafeCode is forbidden".into(),
        });
    }
    if !editor_only {
        let refs = doc
            .get("references")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        for r in refs.iter().filter_map(Value::as_str) {
            if r.starts_with("UnityEditor") {
                hits.push(Hit {
                    rule: "editor-in-runtime".into(),
                    path: rel.into(),
                    line: 0,
                    excerpt: format!("a runtime assembly references {r}"),
                });
            }
        }
    }
    hits
}

fn is_editor_only(doc: &Value) -> bool {
    doc.get("includePlatforms")
        .and_then(Value::as_array)
        .is_some_and(|p| p.len() == 1 && p[0].as_str() == Some("Editor"))
}

/// Credential patterns in any text: etos keys and tickets, `sk-` provider keys, bearer values.
pub fn scan_credentials(rel: &str, text: &str) -> Vec<Hit> {
    let mut hits = Vec::new();
    for (n, line) in text.lines().enumerate() {
        if credential_in(line) {
            let excerpt = mask_secrets(&redact(line.trim()));
            hits.push(Hit {
                rule: "credentials".into(),
                path: rel.into(),
                line: n + 1,
                excerpt: excerpt.chars().take(160).collect(),
            });
        }
    }
    hits
}

fn credential_in(line: &str) -> bool {
    let token_len = |s: &str| {
        s.chars()
            .take_while(|c| {
                c.is_ascii_alphanumeric() || matches!(c, '_' | '-' | '.' | '~' | '+' | '/' | '=')
            })
            .count()
    };
    for prefix in ["etk_", "ett_", "etp_", "eta_"] {
        let mut rest = line;
        while let Some(pos) = rest.find(prefix) {
            let boundary = pos == 0
                || !rest[..pos]
                    .chars()
                    .last()
                    .is_some_and(|c| c.is_ascii_alphanumeric());
            if boundary && token_len(&rest[pos + prefix.len()..]) >= 4 {
                return true;
            }
            rest = &rest[pos + prefix.len()..];
        }
    }
    let mut rest = line;
    while let Some(pos) = rest.find("sk-") {
        let boundary = pos == 0
            || !rest[..pos]
                .chars()
                .last()
                .is_some_and(|c| c.is_ascii_alphanumeric());
        let n = rest[pos + 3..]
            .chars()
            .take_while(|c| c.is_ascii_alphanumeric() || *c == '-' || *c == '_')
            .count();
        if boundary && n >= 16 {
            return true;
        }
        rest = &rest[pos + 3..];
    }
    let lower = line.to_ascii_lowercase();
    let mut from = 0;
    while let Some(pos) = lower[from..].find("bearer ") {
        let at = from + pos + 7;
        if token_len(&line[at..]) >= 8 {
            return true;
        }
        from = at;
    }
    false
}

const NATIVE_EXTENSIONS: &[&str] = &[
    "dll", "so", "dylib", "bundle", "a", "lib", "jar", "aar", "jnilib", "exe",
];

fn owning_asmdef_editor_only(root: &Path, file: &Path) -> bool {
    let mut dir = file.parent();
    while let Some(d) = dir {
        if let Ok(entries) = std::fs::read_dir(d) {
            let mut asmdefs: Vec<PathBuf> = entries
                .filter_map(Result::ok)
                .map(|e| e.path())
                .filter(|p| p.extension().is_some_and(|x| x == "asmdef"))
                .collect();
            asmdefs.sort();
            if let Some(first) = asmdefs.first() {
                return std::fs::read_to_string(first)
                    .ok()
                    .and_then(|t| serde_json::from_str::<Value>(&t).ok())
                    .is_some_and(|doc| is_editor_only(&doc));
            }
        }
        if d == root {
            break;
        }
        dir = d.parent();
    }
    false
}

fn collect(dir: &Path, out: &mut Vec<PathBuf>) -> std::io::Result<()> {
    let mut entries: Vec<_> = std::fs::read_dir(dir)?.filter_map(Result::ok).collect();
    entries.sort_by_key(|e| e.file_name());
    for e in entries {
        let p = e.path();
        let ft = e.file_type()?;
        if ft.is_symlink() {
            out.push(p);
        } else if ft.is_dir() {
            if p.extension()
                .is_some_and(|x| x == "framework" || x == "bundle")
            {
                out.push(p);
            } else {
                collect(&p, out)?;
            }
        } else {
            out.push(p);
        }
    }
    Ok(())
}

/// Scans a package directory.
pub fn scan_package(root: &Path, ctx: &ScanContext) -> std::io::Result<ScanReport> {
    let mut files = Vec::new();
    collect(root, &mut files)?;
    let mut report = ScanReport::default();
    for path in files {
        let rel = path
            .strip_prefix(root)
            .unwrap_or(&path)
            .to_string_lossy()
            .replace('\\', "/");
        let meta = std::fs::symlink_metadata(&path)?;
        let ext = path
            .extension()
            .map(|e| e.to_string_lossy().to_ascii_lowercase())
            .unwrap_or_default();
        if meta.file_type().is_symlink()
            || meta.is_dir()
            || NATIVE_EXTENSIONS.contains(&ext.as_str())
        {
            report.hits.push(Hit {
                rule: "native-plugin".into(),
                path: rel.clone(),
                line: 0,
                excerpt: if meta.file_type().is_symlink() {
                    "a symbolic link".into()
                } else {
                    format!("a native plugin or binary library (.{ext})")
                },
            });
            continue;
        }
        report.files += 1;
        report.bytes += meta.len();
        if meta.len() > MAX_UNMANIFESTED_BLOB && !ctx.blobs.contains(&rel) {
            report.hits.push(Hit {
                rule: "binary-blob".into(),
                path: rel.clone(),
                line: 0,
                excerpt: format!(
                    "{} bytes (> {} without a blobs manifest entry)",
                    meta.len(),
                    MAX_UNMANIFESTED_BLOB
                ),
            });
            continue;
        }
        let bytes = std::fs::read(&path)?;
        let head = &bytes[..bytes.len().min(8192)];
        let Ok(text) = std::str::from_utf8(&bytes) else {
            continue;
        };
        if head.contains(&0) {
            continue;
        }
        report.hits.extend(scan_credentials(&rel, text));
        match ext.as_str() {
            "cs" => {
                let editor = owning_asmdef_editor_only(root, &path);
                let lines: Vec<&str> = text.lines().collect();
                for hit in scan_csharp(&rel, text, editor, ctx) {
                    let line = lines.get(hit.line.saturating_sub(1)).copied().unwrap_or("");
                    match ctx.exemptions.iter().find(|x| x.matches(&hit, text, line)) {
                        Some(x) => report.exempted.push((x.id.clone(), hit)),
                        None => report.hits.push(hit),
                    }
                }
            }
            "asmdef" => report.hits.extend(scan_asmdef(&rel, text, ctx)),
            _ => {}
        }
    }
    report.hits.sort_by(|a, b| {
        (a.path.as_str(), a.line, a.rule.as_str()).cmp(&(b.path.as_str(), b.line, b.rule.as_str()))
    });
    report.hits.dedup();
    Ok(report)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn generated_exemption() -> Exemption {
        Exemption {
            id: "generated-catalog-tables".into(),
            rule: "static-mutable".into(),
            path_suffix: ".g.cs".into(),
            directory: "Generated".into(),
            header:
                "// Generated by GameCore.Content.Compiler.CatalogEmitter; do not edit by hand."
                    .into(),
            excerpt_contains: "static readonly".into(),
            reason: "emitter tables".into(),
        }
    }

    #[test]
    fn an_exemption_covers_only_its_exact_shape() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path();
        std::fs::create_dir_all(root.join("Runtime/Generated")).unwrap();
        let header = "// <auto-generated />\n// Generated by GameCore.Content.Compiler.CatalogEmitter; do not edit by hand.\n";
        let body = "class C {\n    public static readonly int[] Keys = { 1 };\n    public static int counter;\n}\n";
        std::fs::write(
            root.join("Runtime/Generated/C.g.cs"),
            format!("{header}{body}"),
        )
        .unwrap();
        // Same content without the generator header, and with the header in another folder.
        std::fs::write(root.join("Runtime/Generated/D.g.cs"), body).unwrap();
        std::fs::write(root.join("Runtime/E.g.cs"), format!("{header}{body}")).unwrap();
        let mut c = ctx();
        c.exemptions = vec![generated_exemption()];
        let report = scan_package(root, &c).unwrap();
        let exempted: Vec<(&str, usize)> = report
            .exempted
            .iter()
            .map(|(_, h)| (h.path.as_str(), h.line))
            .collect();
        assert_eq!(exempted, vec![("Runtime/Generated/C.g.cs", 4)]);
        // The non-readonly field in the generated file and every hit elsewhere still fire.
        let hits: Vec<(&str, usize)> = report
            .hits
            .iter()
            .map(|h| (h.path.as_str(), h.line))
            .collect();
        assert_eq!(
            hits,
            vec![
                ("Runtime/E.g.cs", 4),
                ("Runtime/E.g.cs", 5),
                ("Runtime/Generated/C.g.cs", 5),
                ("Runtime/Generated/D.g.cs", 2),
                ("Runtime/Generated/D.g.cs", 3),
            ]
        );
    }

    #[test]
    fn the_repository_allowlist_loads_and_bad_entries_are_refused() {
        let repo = Path::new(env!("CARGO_MANIFEST_DIR")).join("../stage/allowlist.json");
        let list = load_exemptions(&repo).unwrap();
        // R2-G removed generated-array exemptions; the trusted manifest now permits none.
        assert!(list.is_empty());
        assert!(
            list.iter()
                .all(|x| RULES.contains(&x.rule.as_str()) && !x.reason.is_empty())
        );
        let dir = tempfile::tempdir().unwrap();
        let bad = dir.path().join("a.json");
        let mut x = serde_json::to_value(generated_exemption()).unwrap();
        x["rule"] = "nope".into();
        std::fs::write(&bad, serde_json::json!({"scanExemptions": [x]}).to_string()).unwrap();
        assert!(load_exemptions(&bad).is_err());
        let mut y = serde_json::to_value(generated_exemption()).unwrap();
        y["reason"] = " ".into();
        std::fs::write(&bad, serde_json::json!({"scanExemptions": [y]}).to_string()).unwrap();
        assert!(load_exemptions(&bad).is_err());
    }

    fn ctx() -> ScanContext {
        ScanContext {
            package: "com.hollowmere.mechanism.pressureplate".into(),
            ..ScanContext::default()
        }
    }

    fn rules(src: &str) -> Vec<String> {
        scan_csharp("Runtime/A.cs", src, false, &ctx())
            .into_iter()
            .map(|h| h.rule)
            .collect()
    }

    #[test]
    fn every_rule_has_a_positive_case() {
        let cases: &[(&str, &str)] = &[
            (
                "reflection-emit",
                "using System.Reflection.Emit;\nclass A {}",
            ),
            (
                "reflection-emit",
                "class A { void F() { var g = default(ILGenerator); } }",
            ),
            (
                "process-start",
                "class A { void F() { System.Diagnostics.Process.Start(\"curl\"); } }",
            ),
            (
                "process-start",
                "class A { void F() { Process . Start(\"x\"); } }",
            ),
            (
                "process-start",
                "class A { object o = new ProcessStartInfo(); }",
            ),
            (
                "file-write",
                "class A { void F() { System.IO.File.WriteAllText(\"/tmp/x\", \"y\"); } }",
            ),
            ("file-write", "class A { void F() { File.Delete(path); } }"),
            (
                "file-write",
                "class A { void F() { Directory.CreateDirectory(\"Assets/Other/x\"); } }",
            ),
            (
                "file-write",
                "class A { void F() { using var s = new FileStream(p, m); } }",
            ),
            ("editor-in-runtime", "using UnityEditor;\nclass A {}"),
            (
                "dllimport",
                "class A { [DllImport(\"libc\")] static extern int getpid(); }",
            ),
            ("unsafe", "class A { unsafe void F() {} }"),
            (
                "unsafe",
                "class A { void F() { int* p = stackalloc int[4]; } }",
            ),
            ("network", "using System.Net.Http;\nclass A {}"),
            (
                "network",
                "class A { void F() { UnityWebRequest.Get(u); } }",
            ),
            ("network", "using UnityEngine.Networking;\nclass A {}"),
            (
                "resources-absolute",
                "class A { void F() { Resources.Load(\"/etc/passwd\"); } }",
            ),
            (
                "resources-absolute",
                "class A { void F() { Resources.Load<T>(\"../x\"); } }",
            ),
            (
                "static-mutable",
                "class A { private static int pressCount; }",
            ),
            (
                "static-mutable",
                "class A { public static string Name = \"x\"; }",
            ),
            (
                "static-mutable",
                "class A { static readonly List<int> All = new List<int>(); }",
            ),
            (
                "static-mutable",
                "class A { private static readonly int[] Table = { 1 }; }",
            ),
            (
                "static-mutable",
                "class A { public static event System.Action Fired; }",
            ),
            ("static-mutable", "class A { [ThreadStatic] static int t; }"),
            (
                "static-mutable",
                "class A { [SerializeField] private static Dictionary<string, int> map; }",
            ),
        ];
        for (rule, src) in cases {
            assert!(
                rules(src).iter().any(|r| r == rule),
                "{rule} not found in: {src} -> {:?}",
                rules(src)
            );
        }
    }

    #[test]
    fn r2_11_textual_paths_and_editor_hooks_never_bypass_prefilter() {
        for source in [
            r#"File.WriteAllText("Assets/com.test/../../Editor/evil.cs", "x");"#,
            r#"File.WriteAllText("/outside", Application.persistentDataPath);"#,
            "[InitializeOnLoad] class Evil {}",
            "class Evil : AssetPostprocessor {}",
            "File.ReadAllText(path);",
        ] {
            assert!(
                !scan_csharp("Editor/Evil.cs", source, true, &ctx()).is_empty(),
                "{source}"
            );
        }
    }

    #[test]
    fn legitimate_code_is_not_a_hit() {
        let clean = r#"#nullable enable
// Process.Start in a comment, System.Net in a comment, unsafe in a comment.
/* File.Delete("x"); static int nope; */
using System;
using System.Collections.Generic;
using static System.Math;
namespace Hollowmere.Mechanism.PressurePlate
{
    public static class Plate
    {
        public const int Threshold = 1;
        public static readonly Id128 Owner = Ids.Of("plate");
        private static readonly string Name = "plate \"Process.Start\" text";
        public static int Weight { get; } = 3;
        public static int Twice(int x) => x * 2;
        public static string Label => "x";
        static int Local(int x) { static int Inner(int y) => y; return Inner(x); }
        public static IReadOnlyList<int> Slots() { return new List<int> { 1 }; }
        private static readonly Func<int, int> Square = static x => x * x;
        public static void Res() { UnityEngine.Resources.Load("Plates/Default"); }
        public static string Url = "sk-short";
    }
}
"#;
        let found: Vec<Hit> = scan_csharp("Runtime/Plate.cs", clean, false, &ctx())
            .into_iter()
            .filter(|h| h.rule != "static-mutable" || !h.excerpt.contains("Url"))
            .collect();
        assert!(found.is_empty(), "{found:?}");
        // The deliberately mutable field at the end is found.
        let url: Vec<Hit> = scan_csharp("Runtime/Plate.cs", clean, false, &ctx());
        assert_eq!(url.len(), 1, "{url:?}");
        assert!(url[0].excerpt.contains("Url"));
    }

    #[test]
    fn unity_editor_is_legal_in_editor_code_only() {
        let src = "using UnityEditor;\nclass A {}";
        assert!(scan_csharp("Editor/A.cs", src, false, &ctx()).is_empty());
        assert!(scan_csharp("Runtime/A.cs", src, true, &ctx()).is_empty());
        let guarded = "#if UNITY_EDITOR\nusing UnityEditor;\n#endif\nclass A {}";
        assert!(scan_csharp("Runtime/A.cs", guarded, false, &ctx()).is_empty());
        let negated = "#if !UNITY_EDITOR\nusing UnityEditor;\n#endif\nclass A {}";
        assert_eq!(scan_csharp("Runtime/A.cs", negated, false, &ctx()).len(), 1);
        let else_branch = "#if UNITY_EDITOR\nint a;\n#else\nusing UnityEditor;\n#endif\n";
        assert_eq!(
            scan_csharp("Runtime/A.cs", else_branch, false, &ctx()).len(),
            1
        );
    }

    #[test]
    fn r2_11_unsafe_reason_never_grants_authority() {
        let src = "class A { unsafe void F() {} }";
        let mut with_reason = ctx();
        with_reason.allow_unsafe = Some("SIMD ring buffer".into());
        assert_eq!(
            scan_csharp("Runtime/A.cs", src, false, &with_reason).len(),
            1
        );
        with_reason.allow_unsafe = Some("  ".into());
        assert_eq!(
            scan_csharp("Runtime/A.cs", src, false, &with_reason).len(),
            1
        );
        let asmdef = r#"{"name":"A","allowUnsafeCode":true,"references":["UnityEditor.UI"]}"#;
        let hits = scan_asmdef("Runtime/A.asmdef", asmdef, &ctx());
        let rules: Vec<&str> = hits.iter().map(|h| h.rule.as_str()).collect();
        assert_eq!(rules, vec!["unsafe", "editor-in-runtime"]);
        let editor = r#"{"name":"A.Editor","includePlatforms":["Editor"],"references":["UnityEditor.TestRunner"]}"#;
        assert!(scan_asmdef("Editor/A.asmdef", editor, &ctx()).is_empty());
    }

    #[test]
    fn credentials_are_found_and_redacted() {
        let text = "key = etk_abcdef123456\nurl = \"sk-ABCDEFGHIJKLMNOPQRST\"\nAuthorization: Bearer abc.def.ghi\nnot: sk-short desk_ett\n";
        let hits = scan_credentials("Runtime/Config.json", text);
        assert_eq!(
            hits.iter().map(|h| h.line).collect::<Vec<_>>(),
            vec![1, 2, 3]
        );
        for h in &hits {
            assert!(!h.excerpt.contains("abcdef123456"), "{}", h.excerpt);
            assert!(!h.excerpt.contains("ABCDEFGHIJKLMNOPQRST"), "{}", h.excerpt);
            assert!(!h.excerpt.contains("abc.def.ghi"), "{}", h.excerpt);
        }
    }

    #[test]
    fn package_scan_covers_blobs_native_plugins_and_asmdefs() {
        let dir = tempfile::tempdir().unwrap();
        let root = dir.path();
        std::fs::create_dir_all(root.join("Runtime")).unwrap();
        std::fs::create_dir_all(root.join("Editor")).unwrap();
        std::fs::create_dir_all(root.join("Plugins")).unwrap();
        std::fs::write(root.join("package.json"), "{\"name\":\"com.x.y\"}").unwrap();
        std::fs::write(root.join("Runtime/R.asmdef"), "{\"name\":\"R\"}").unwrap();
        std::fs::write(
            root.join("Runtime/R.cs"),
            "using UnityEditor;\nclass R { static int s; }",
        )
        .unwrap();
        std::fs::write(
            root.join("Editor/E.asmdef"),
            "{\"name\":\"E\",\"includePlatforms\":[\"Editor\"]}",
        )
        .unwrap();
        std::fs::write(root.join("Editor/E.cs"), "using UnityEditor;\nclass E {}").unwrap();
        std::fs::write(root.join("Plugins/native.so"), [0u8, 1, 2]).unwrap();
        std::fs::write(
            root.join("big.bytes"),
            vec![b'a'; (MAX_UNMANIFESTED_BLOB + 1) as usize],
        )
        .unwrap();
        std::fs::write(
            root.join("declared.bytes"),
            vec![b'b'; (MAX_UNMANIFESTED_BLOB + 1) as usize],
        )
        .unwrap();
        let mut c = ctx();
        c.blobs.insert("declared.bytes".into());
        let report = scan_package(root, &c).unwrap();
        let found: Vec<(String, String)> = report
            .hits
            .iter()
            .map(|h| (h.rule.clone(), h.path.clone()))
            .collect();
        assert_eq!(
            found,
            vec![
                ("native-plugin".to_string(), "Plugins/native.so".to_string()),
                ("editor-in-runtime".to_string(), "Runtime/R.cs".to_string()),
                ("static-mutable".to_string(), "Runtime/R.cs".to_string()),
                ("binary-blob".to_string(), "big.bytes".to_string()),
            ]
        );
        assert_eq!(report.files, 7);
    }

    #[test]
    fn rule_list_is_complete() {
        assert_eq!(RULES.len(), 14);
        let unique: BTreeSet<&&str> = RULES.iter().collect();
        assert_eq!(unique.len(), RULES.len());
    }
}
