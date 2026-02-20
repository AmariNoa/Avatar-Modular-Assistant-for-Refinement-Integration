using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace com.amari_noa.avatar_modular_assistant.editor
{
    [InitializeOnLoad]
    public static class AmariLocalization
    {
        private const string LocalizationDirGuid = "0493772b8f41ac54a814625e5072574d";
        private static readonly string LocalizationDirRoot = AssetDatabase.GUIDToAssetPath(LocalizationDirGuid);

        private const string DefaultLanguageCode = "en-US";
        public static List<string> LanguageCodes { get; } = new();

        private static string _currentLanguageCode;
        private static Dictionary<string, string> _textTable;

        static AmariLocalization()
        {
            EnsureLoaded();
        }

        public static string CurrentLanguageCode
        {
            get
            {
                EnsureLoaded();
                return _currentLanguageCode;
            }
        }

        public static bool LoadLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                languageCode = DefaultLanguageCode;

            if (LanguageCodes.Count > 0 && !LanguageCodes.Contains(languageCode))
            {
                Debug.LogWarning($"[AMARI] Unknown language code: {languageCode}");
                languageCode = DefaultLanguageCode;
            }

            var path = Path.Combine(LocalizationDirRoot, $"{languageCode}.json");
            var text = LoadTextFromPackagePath(path);
            if (text == null)
            {
                Debug.LogWarning($"[AMARI] Localization json not found: {path}");
                return false;
            }

            if (!TryParseAndBuildTable(text, out var table, out var error))
            {
                Debug.LogError($"[AMARI] Localization json parse failed ({languageCode}): {error}");
                return false;
            }

            _currentLanguageCode = languageCode;
            _textTable = table;
            return true;
        }

        public static string Get(string key, string fallback = null)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(key))
                return fallback ?? string.Empty;

            if (_textTable != null && _textTable.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;

            return fallback ?? key;
        }

        public static void LoadFromEditorLocale()
        {
            var uiCulture = CultureInfo.CurrentUICulture;
            var code = uiCulture.Name;
            if (!LoadLanguage(code))
            {
                LoadLanguage(DefaultLanguageCode);
            }
        }

        private static void EnsureLoaded()
        {
            if (LanguageCodes.Count == 0)
            {
                foreach (var file in Directory.EnumerateFiles(LocalizationDirRoot, "*.json"))
                {
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                    LanguageCodes.Add(fileNameWithoutExtension);
                }
            }

            if (_textTable != null && !string.IsNullOrEmpty(_currentLanguageCode))
            {
                return;
            }

            LoadFromEditorLocale();
            if (_textTable != null)
            {
                return;
            }

            _currentLanguageCode = DefaultLanguageCode;
            _textTable = new Dictionary<string, string>();
        }

        private static string LoadTextFromPackagePath(string packageRelativePath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(packageRelativePath);
            return asset != null ? asset.text : null;
        }

        private static bool TryParseAndBuildTable(string json, out Dictionary<string, string> table, out string error)
        {
            table = null;
            error = null;

            object root;
            try
            {
                root = MiniJson.Deserialize(json);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (root is not IDictionary<string, object> dict)
            {
                error = "Root must be a JSON object.";
                return false;
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(dict, "", result);
            table = result;
            return true;
        }

        private static void Flatten(IDictionary<string, object> node, string prefix, Dictionary<string, string> dst)
        {
            foreach (var (s, val) in node)
            {
                var key = string.IsNullOrEmpty(prefix) ? s : $"{prefix}.{s}";

                switch (val)
                {
                    case IDictionary<string, object> childObj:
                        Flatten(childObj, key, dst);
                        break;
                    case IList childArr:
                    {
                        for (var i = 0; i < childArr.Count; i++)
                        {
                            var arrKey = $"{key}.{i}";
                            if (childArr[i] is IDictionary<string, object> arrObj)
                                Flatten(arrObj, arrKey, dst);
                            else
                                dst[arrKey] = childArr[i]?.ToString() ?? string.Empty;
                        }

                        break;
                    }
                    default:
                        dst[key] = val?.ToString() ?? string.Empty;
                        break;
                }
            }
        }

        private static class MiniJson
        {
            public static object Deserialize(string json)
            {
                return json == null ? null : new Parser(json).ParseValue();
            }

            private sealed class Parser
            {
                private readonly string _json;
                private int _index;

                public Parser(string json)
                {
                    _json = json;
                    _index = 0;
                    SkipWhitespace();
                }

                public object ParseValue()
                {
                    SkipWhitespace();
                    if (_index >= _json.Length) return null;

                    var c = _json[_index];
                    return c switch
                    {
                        '{' => ParseObject(),
                        '[' => ParseArray(),
                        '"' => ParseString(),
                        _ => throw new FormatException($"Only string values are supported (at index {_index}).")
                    };
                }

                private IDictionary<string, object> ParseObject()
                {
                    Expect('{');
                    SkipWhitespace();

                    var obj = new Dictionary<string, object>(StringComparer.Ordinal);

                    if (Peek() == '}')
                    {
                        _index++;
                        return obj;
                    }

                    while (true)
                    {
                        SkipWhitespace();
                        var key = ParseString();
                        SkipWhitespace();
                        Expect(':');
                        SkipWhitespace();
                        var value = ParseValue();
                        obj[key] = value;

                        SkipWhitespace();
                        var ch = Peek();
                        if (ch == ',')
                        {
                            _index++;
                            continue;
                        }

                        if (ch != '}')
                        {
                            throw new FormatException($"Invalid object at index {_index}");
                        }

                        _index++;
                        break;

                    }

                    return obj;
                }

                private IList ParseArray()
                {
                    Expect('[');
                    SkipWhitespace();

                    var list = new List<object>();

                    if (Peek() == ']')
                    {
                        _index++;
                        return list;
                    }

                    while (true)
                    {
                        SkipWhitespace();
                        var v = ParseValue();
                        list.Add(v);

                        SkipWhitespace();
                        var ch = Peek();
                        if (ch == ',')
                        {
                            _index++;
                            continue;
                        }

                        if (ch != ']')
                        {
                            throw new FormatException($"Invalid array at index {_index}");
                        }

                        _index++;
                        break;

                    }

                    return list;
                }

                private string ParseString()
                {
                    Expect('"');
                    var sb = new StringBuilder();

                    while (_index < _json.Length)
                    {
                        var c = _json[_index++];
                        if (c == '"')
                            return sb.ToString();

                        if (c == '\\')
                        {
                            if (_index >= _json.Length) break;
                            var esc = _json[_index++];

                            switch (esc)
                            {
                                case '"': sb.Append('\"'); break;
                                case '\\': sb.Append('\\'); break;
                                case '/': sb.Append('/'); break;
                                case 'b': sb.Append('\b'); break;
                                case 'f': sb.Append('\f'); break;
                                case 'n': sb.Append('\n'); break;
                                case 'r': sb.Append('\r'); break;
                                case 't': sb.Append('\t'); break;
                                case 'u':
                                    if (_index + 4 > _json.Length) throw new FormatException("Invalid unicode escape.");
                                    var hex = _json.Substring(_index, 4);
                                    sb.Append((char)Convert.ToInt32(hex, 16));
                                    _index += 4;
                                    break;
                                default:
                                    throw new FormatException($"Invalid escape '\\{esc}' at index {_index}");
                            }

                            continue;
                        }

                        sb.Append(c);
                    }

                    throw new FormatException("Unterminated string.");
                }

                private void SkipWhitespace()
                {
                    while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                        _index++;
                }

                private char Peek() => _index < _json.Length ? _json[_index] : '\0';

                private void Expect(char c)
                {
                    if (Peek() != c)
                        throw new FormatException($"Expected '{c}' at index {_index}");
                    _index++;
                }
            }
        }
    }
}
