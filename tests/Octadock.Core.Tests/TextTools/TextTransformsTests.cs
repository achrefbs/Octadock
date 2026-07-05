using FluentAssertions;
using Octadock.Core.TextTools;
using Xunit;

namespace Octadock.Core.Tests.TextTools;

public class TextTransformsTests
{
    // ---- JSON ------------------------------------------------------------

    [Fact]
    public void JsonPretty_indents_valid_json()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.JsonPretty, "{\"a\":[1,2]}");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("\"a\": [");
        result.Output.Should().Contain("\n");
    }

    [Fact]
    public void JsonPretty_accepts_trailing_commas_and_comments()
    {
        TextTransformResult result = TextTransforms.Apply(
            TextTransformKind.JsonPretty,
            "{\n// comment\n\"a\": 1,\n}");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("\"a\": 1");
    }

    [Fact]
    public void JsonPretty_reports_line_and_column_for_invalid_json()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.JsonPretty, "{\n  \"a\": oops\n}");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("line 2");
    }

    [Fact]
    public void JsonMinify_collapses_whitespace()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.JsonMinify, "{\n  \"a\": 1\n}");

        result.Success.Should().BeTrue();
        result.Output.Should().Be("{\"a\":1}");
    }

    [Fact]
    public void Json_transforms_fail_on_empty_input()
    {
        TextTransforms.Apply(TextTransformKind.JsonPretty, "  ").Success.Should().BeFalse();
        TextTransforms.Apply(TextTransformKind.JsonMinify, string.Empty).Success.Should().BeFalse();
    }

    // ---- Base64 / URL / HTML ----------------------------------------------

    [Fact]
    public void Base64_round_trips_utf8_text()
    {
        TextTransformResult encoded = TextTransforms.Apply(TextTransformKind.Base64Encode, "héllo wörld");
        TextTransformResult decoded = TextTransforms.Apply(TextTransformKind.Base64Decode, encoded.Output);

        decoded.Success.Should().BeTrue();
        decoded.Output.Should().Be("héllo wörld");
    }

    [Fact]
    public void Base64Decode_accepts_url_safe_alphabet_without_padding()
    {
        // "ab?cd>" encodes to "YWI/Y2Q+" standard, "YWI_Y2Q-" url-safe.
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.Base64Decode, "YWI_Y2Q-");

        result.Success.Should().BeTrue();
        result.Output.Should().Be("ab?cd>");
    }

    [Fact]
    public void Base64Decode_rejects_invalid_input()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.Base64Decode, "!!!not base64!!!");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Base64");
    }

    [Fact]
    public void Base64Decode_renders_binary_payloads_as_hex_dump()
    {
        // 0xFF 0xFE is not valid UTF-8.
        string encoded = Convert.ToBase64String([0xFF, 0xFE]);

        TextTransformResult result = TextTransforms.Apply(TextTransformKind.Base64Decode, encoded);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("2 bytes");
        result.Output.Should().Contain("ff fe");
    }

    [Fact]
    public void Url_encode_and_decode_round_trip()
    {
        TextTransformResult encoded = TextTransforms.Apply(TextTransformKind.UrlEncode, "a b&c=d");
        encoded.Output.Should().Be("a%20b%26c%3Dd");

        TextTransformResult decoded = TextTransforms.Apply(TextTransformKind.UrlDecode, encoded.Output);
        decoded.Output.Should().Be("a b&c=d");
    }

    [Fact]
    public void UrlDecode_treats_plus_as_space()
    {
        TextTransforms.Apply(TextTransformKind.UrlDecode, "a+b").Output.Should().Be("a b");
    }

    [Fact]
    public void Html_encode_and_decode_round_trip()
    {
        TextTransformResult encoded = TextTransforms.Apply(TextTransformKind.HtmlEncode, "<a & \"b\">");
        encoded.Output.Should().Be("&lt;a &amp; &quot;b&quot;&gt;");

        TextTransforms.Apply(TextTransformKind.HtmlDecode, encoded.Output).Output.Should().Be("<a & \"b\">");
    }

    // ---- JWT -----------------------------------------------------------------

    [Fact]
    public void JwtDecode_shows_header_and_payload()
    {
        // {"alg":"HS256","typ":"JWT"} . {"sub":"42","name":"Ada"} . <sig>
        const string Token =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI0MiIsIm5hbWUiOiJBZGEifQ.c2ln";

        TextTransformResult result = TextTransforms.Apply(TextTransformKind.JwtDecode, Token);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("\"alg\": \"HS256\"");
        result.Output.Should().Contain("\"name\": \"Ada\"");
        result.Output.Should().Contain("not verified");
    }

    [Fact]
    public void JwtDecode_strips_bearer_prefix()
    {
        const string Token =
            "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI0MiIsIm5hbWUiOiJBZGEifQ.c2ln";

        TextTransforms.Apply(TextTransformKind.JwtDecode, Token).Success.Should().BeTrue();
    }

    [Fact]
    public void JwtDecode_rejects_non_jwt_text()
    {
        TextTransforms.Apply(TextTransformKind.JwtDecode, "hello world").Success.Should().BeFalse();
    }

    // ---- Casing -----------------------------------------------------------------

    [Theory]
    [InlineData(TextTransformKind.CamelCase, "hello world example", "helloWorldExample")]
    [InlineData(TextTransformKind.PascalCase, "hello world example", "HelloWorldExample")]
    [InlineData(TextTransformKind.SnakeCase, "Hello World Example", "hello_world_example")]
    [InlineData(TextTransformKind.KebabCase, "Hello World Example", "hello-world-example")]
    [InlineData(TextTransformKind.ConstantCase, "hello world", "HELLO_WORLD")]
    [InlineData(TextTransformKind.SnakeCase, "XMLHttpRequest", "xml_http_request")]
    [InlineData(TextTransformKind.CamelCase, "user_id-value", "userIdValue")]
    [InlineData(TextTransformKind.PascalCase, "alreadyCamelCase", "AlreadyCamelCase")]
    public void Identifier_casing_handles_word_boundaries(TextTransformKind kind, string input, string expected)
    {
        TextTransformResult result = TextTransforms.Apply(kind, input);

        result.Success.Should().BeTrue();
        result.Output.Should().Be(expected);
    }

    [Fact]
    public void Upper_lower_and_title_case_work()
    {
        TextTransforms.Apply(TextTransformKind.UpperCase, "abc").Output.Should().Be("ABC");
        TextTransforms.Apply(TextTransformKind.LowerCase, "ABC").Output.Should().Be("abc");
        TextTransforms.Apply(TextTransformKind.TitleCase, "hello WORLD").Output.Should().Be("Hello World");
    }

    [Fact]
    public void Casing_fails_cleanly_when_no_words_exist()
    {
        TextTransforms.Apply(TextTransformKind.SnakeCase, "!!! ---").Success.Should().BeFalse();
    }

    // ---- Hashes ----------------------------------------------------------------

    [Theory]
    [InlineData(TextTransformKind.HashMd5, "abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData(TextTransformKind.HashSha1, "abc", "a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData(
        TextTransformKind.HashSha256,
        "abc",
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Hashes_match_known_vectors(TextTransformKind kind, string input, string expected)
    {
        TextTransforms.Apply(kind, input).Output.Should().Be(expected);
    }

    [Fact]
    public void Sha512_produces_128_hex_chars()
    {
        TextTransforms.Apply(TextTransformKind.HashSha512, "abc").Output.Should().HaveLength(128);
    }

    // ---- Timestamps ---------------------------------------------------------------

    [Fact]
    public void UnixToDate_reads_seconds()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.UnixToDate, "0");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("1970-01-01T00:00:00Z UTC");
        result.Output.Should().Contain("seconds");
    }

    [Fact]
    public void UnixToDate_reads_milliseconds_for_large_values()
    {
        // 2026-07-04T00:00:00Z in milliseconds.
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.UnixToDate, "1783123200000");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("2026-07-04T00:00:00Z UTC");
        result.Output.Should().Contain("milliseconds");
    }

    [Fact]
    public void UnixToDate_rejects_non_numeric_input()
    {
        TextTransforms.Apply(TextTransformKind.UnixToDate, "yesterday").Success.Should().BeFalse();
    }

    [Fact]
    public void DateToUnix_converts_iso_dates()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.DateToUnix, "1970-01-02T00:00:00Z");

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("86400 seconds");
        result.Output.Should().Contain("86400000 milliseconds");
    }

    [Fact]
    public void DateToUnix_rejects_unparseable_dates()
    {
        TextTransforms.Apply(TextTransformKind.DateToUnix, "not a date").Success.Should().BeFalse();
    }

    // ---- Line utilities --------------------------------------------------------------

    [Fact]
    public void SortLines_orders_case_insensitively()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.SortLines, "banana\nApple\ncherry");

        result.Output.Should().Be($"Apple{Environment.NewLine}banana{Environment.NewLine}cherry");
    }

    [Fact]
    public void DedupeLines_keeps_first_occurrence()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.DedupeLines, "a\nb\na\nc\nb");

        result.Output.Should().Be($"a{Environment.NewLine}b{Environment.NewLine}c");
    }

    [Fact]
    public void ReverseLines_reverses_order()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.ReverseLines, "1\n2\n3");

        result.Output.Should().Be($"3{Environment.NewLine}2{Environment.NewLine}1");
    }

    [Fact]
    public void TrimLines_strips_trailing_spaces_and_outer_blank_lines()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.TrimLines, "\n  a  \nb\t\n\n");

        result.Output.Should().Be($"  a{Environment.NewLine}b");
    }

    [Fact]
    public void CountStats_reports_chars_words_lines()
    {
        TextTransformResult result = TextTransforms.Apply(TextTransformKind.CountStats, "one two\nthree");

        result.Output.Should().Contain("13 characters");
        result.Output.Should().Contain("3 words");
        result.Output.Should().Contain("2 lines");
    }

    // ---- Catalog ---------------------------------------------------------------------

    [Fact]
    public void Catalog_covers_every_transform_kind_exactly_once()
    {
        TextTransformKind[] catalogKinds = TextTransforms.Catalog.Select(d => d.Kind).ToArray();

        catalogKinds.Should().OnlyHaveUniqueItems();
        catalogKinds.Should().BeEquivalentTo(Enum.GetValues<TextTransformKind>());
    }

    [Fact]
    public void Every_catalog_entry_has_name_category_and_description()
    {
        foreach (TextTransformDescriptor descriptor in TextTransforms.Catalog)
        {
            descriptor.Name.Should().NotBeNullOrWhiteSpace();
            descriptor.Category.Should().NotBeNullOrWhiteSpace();
            descriptor.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void SplitWords_handles_mixed_identifiers()
    {
        TextTransforms.SplitWords("XMLHttpRequest v2_final-DRAFT")
            .Should().Equal("XML", "Http", "Request", "v2", "final", "DRAFT");
    }
}
