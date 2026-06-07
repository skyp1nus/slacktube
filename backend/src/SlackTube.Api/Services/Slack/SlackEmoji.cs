using System.Text.RegularExpressions;

namespace SlackTube.Api.Services.Slack;

/// <summary>
/// Converts Slack emoji shortcodes (<c>:fire:</c>) to real Unicode so sinks like YouTube render 🔥 instead
/// of the literal ":fire:". Slack's Events API delivers standard emoji as their colon shortcode in the raw
/// message text. Only KNOWN shortcodes are replaced — unknown/custom ones, and colon-delimited text that
/// isn't an emoji (e.g. "16:9"), are left untouched. Slack skin-tone modifiers (:skin-tone-3:) are dropped.
/// </summary>
public static partial class SlackEmoji
{
    public static string ShortcodesToUnicode(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(':')) return text ?? string.Empty;
        return ShortcodePattern().Replace(text, m =>
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith("skin-tone-", StringComparison.Ordinal)) return string.Empty;
            return Map.TryGetValue(name, out var glyph) ? glyph : m.Value; // unknown → leave verbatim
        });
    }

    // Slack shortcode chars: lowercase letters, digits, '_', '+', '-'.
    [GeneratedRegex(@":([a-z0-9_+\-]+):")]
    private static partial Regex ShortcodePattern();

    /// <summary>Common Slack/Unicode shortcodes (the set realistically used in video descriptions). Not
    /// exhaustive by design — unknown codes pass through unchanged rather than guessing.</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        // reactions / symbols
        ["+1"] = "👍", ["thumbsup"] = "👍", ["-1"] = "👎", ["thumbsdown"] = "👎",
        ["fire"] = "🔥", ["100"] = "💯", ["tada"] = "🎉", ["confetti_ball"] = "🎊",
        ["rocket"] = "🚀", ["sparkles"] = "✨", ["star"] = "⭐", ["star2"] = "🌟",
        ["zap"] = "⚡", ["boom"] = "💥", ["dizzy"] = "💫", ["bulb"] = "💡", ["eyes"] = "👀",
        ["white_check_mark"] = "✅", ["heavy_check_mark"] = "✔️", ["ballot_box_with_check"] = "☑️",
        ["x"] = "❌", ["negative_squared_cross_mark"] = "❎", ["warning"] = "⚠️",
        ["exclamation"] = "❗", ["heavy_exclamation_mark"] = "❗", ["bangbang"] = "‼️",
        ["question"] = "❓", ["grey_question"] = "❔", ["no_entry"] = "⛔", ["no_entry_sign"] = "🚫",
        ["stop_sign"] = "🛑", ["construction"] = "🚧", ["recycle"] = "♻️", ["heavy_plus_sign"] = "➕",
        ["heavy_minus_sign"] = "➖", ["heavy_multiplication_x"] = "✖️", ["heavy_division_sign"] = "➗",
        ["arrow_up"] = "⬆️", ["arrow_down"] = "⬇️", ["arrow_left"] = "⬅️", ["arrow_right"] = "➡️",
        ["arrow_forward"] = "▶️", ["rewind"] = "⏪", ["fast_forward"] = "⏩",
        ["small_red_triangle"] = "🔺", ["small_red_triangle_down"] = "🔻",
        ["red_circle"] = "🔴", ["large_blue_circle"] = "🔵", ["white_circle"] = "⚪",
        ["black_circle"] = "⚫", ["green_circle"] = "🟢", ["yellow_circle"] = "🟡", ["orange_circle"] = "🟠",

        // hearts
        ["heart"] = "❤️", ["orange_heart"] = "🧡", ["yellow_heart"] = "💛", ["green_heart"] = "💚",
        ["blue_heart"] = "💙", ["purple_heart"] = "💜", ["black_heart"] = "🖤", ["white_heart"] = "🤍",
        ["broken_heart"] = "💔", ["two_hearts"] = "💕", ["sparkling_heart"] = "💖", ["heartpulse"] = "💗",

        // faces
        ["smile"] = "😄", ["smiley"] = "😃", ["grin"] = "😁", ["grinning"] = "😀",
        ["laughing"] = "😆", ["satisfied"] = "😆", ["sweat_smile"] = "😅", ["joy"] = "😂", ["rofl"] = "🤣",
        ["wink"] = "😉", ["blush"] = "😊", ["slightly_smiling_face"] = "🙂", ["upside_down_face"] = "🙃",
        ["heart_eyes"] = "😍", ["star_struck"] = "🤩", ["kissing_heart"] = "😘", ["yum"] = "😋",
        ["sunglasses"] = "😎", ["smirk"] = "😏", ["thinking_face"] = "🤔", ["thinking"] = "🤔",
        ["neutral_face"] = "😐", ["expressionless"] = "😑", ["unamused"] = "😒", ["roll_eyes"] = "🙄",
        ["grimacing"] = "😬", ["relieved"] = "😌", ["pensive"] = "😔", ["confused"] = "😕",
        ["worried"] = "😟", ["cry"] = "😢", ["sob"] = "😭", ["scream"] = "😱", ["flushed"] = "😳",
        ["angry"] = "😠", ["rage"] = "😡", ["triumph"] = "😤", ["exploding_head"] = "🤯",
        ["partying_face"] = "🥳", ["face_with_raised_eyebrow"] = "🤨", ["wave"] = "👋",

        // hands / gestures
        ["clap"] = "👏", ["raised_hands"] = "🙌", ["pray"] = "🙏", ["muscle"] = "💪",
        ["ok_hand"] = "👌", ["v"] = "✌️", ["crossed_fingers"] = "🤞", ["metal"] = "🤘",
        ["call_me_hand"] = "🤙", ["point_up"] = "☝️", ["point_down"] = "👇", ["point_left"] = "👈",
        ["point_right"] = "👉", ["raised_hand"] = "✋", ["handshake"] = "🤝", ["writing_hand"] = "✍️",
        ["fist"] = "✊", ["facepunch"] = "👊", ["punch"] = "👊",

        // media / tech
        ["movie_camera"] = "🎥", ["clapper"] = "🎬", ["video_camera"] = "📹", ["camera"] = "📷",
        ["tv"] = "📺", ["headphones"] = "🎧", ["microphone"] = "🎤", ["musical_note"] = "🎵",
        ["notes"] = "🎶", ["loud_sound"] = "🔊", ["speaker"] = "🔈", ["mute"] = "🔇",
        ["art"] = "🎨", ["computer"] = "💻", ["desktop_computer"] = "🖥️", ["iphone"] = "📱",
        ["camera_flash"] = "📸", ["film_projector"] = "📽️", ["satellite"] = "📡",

        // objects / money / awards
        ["money_with_wings"] = "💸", ["moneybag"] = "💰", ["dollar"] = "💵", ["credit_card"] = "💳",
        ["gem"] = "💎", ["crown"] = "👑", ["trophy"] = "🏆", ["medal"] = "🏅",
        ["first_place_medal"] = "🥇", ["second_place_medal"] = "🥈", ["third_place_medal"] = "🥉",
        ["dart"] = "🎯", ["chart_with_upwards_trend"] = "📈", ["chart_with_downwards_trend"] = "📉",
        ["bar_chart"] = "📊", ["clipboard"] = "📋", ["calendar"] = "📅", ["date"] = "📆",
        ["bell"] = "🔔", ["no_bell"] = "🔕", ["lock"] = "🔒", ["unlock"] = "🔓", ["key"] = "🔑",
        ["link"] = "🔗", ["paperclip"] = "📎", ["pushpin"] = "📌", ["round_pushpin"] = "📍",
        ["mag"] = "🔍", ["light_bulb"] = "💡", ["gift"] = "🎁", ["balloon"] = "🎈", ["birthday"] = "🎂",
        ["package"] = "📦", ["email"] = "✉️", ["envelope"] = "✉️", ["mailbox"] = "📬",
        ["memo"] = "📝", ["pencil"] = "📝", ["pencil2"] = "✏️", ["books"] = "📚", ["book"] = "📖",
        ["newspaper"] = "📰", ["bookmark"] = "🔖", ["label"] = "🏷️", ["hammer"] = "🔨",
        ["wrench"] = "🔧", ["gear"] = "⚙️", ["nut_and_bolt"] = "🔩", ["telephone"] = "☎️",
        ["phone"] = "☎️", ["hourglass"] = "⌛", ["hourglass_flowing_sand"] = "⏳",
        ["stopwatch"] = "⏱️", ["alarm_clock"] = "⏰", ["watch"] = "⌚", ["zzz"] = "💤",
        ["speech_balloon"] = "💬", ["thought_balloon"] = "💭", ["mega"] = "📣", ["loudspeaker"] = "📢",

        // nature / food
        ["sunny"] = "☀️", ["sun"] = "☀️", ["partly_sunny"] = "⛅", ["cloud"] = "☁️",
        ["rain_cloud"] = "🌧️", ["snowflake"] = "❄️", ["rainbow"] = "🌈", ["ocean"] = "🌊",
        ["crescent_moon"] = "🌙", ["earth_americas"] = "🌎", ["globe_with_meridians"] = "🌐",
        ["seedling"] = "🌱", ["evergreen_tree"] = "🌲", ["four_leaf_clover"] = "🍀",
        ["rose"] = "🌹", ["sunflower"] = "🌻", ["coffee"] = "☕", ["beer"] = "🍺", ["beers"] = "🍻",
        ["wine_glass"] = "🍷", ["cocktail"] = "🍸", ["pizza"] = "🍕", ["hamburger"] = "🍔",
        ["cake"] = "🍰", ["checkered_flag"] = "🏁", ["triangular_flag_on_post"] = "🚩",

        // sports
        ["soccer"] = "⚽", ["basketball"] = "🏀", ["football"] = "🏈", ["baseball"] = "⚾",
        ["tennis"] = "🎾", ["game_die"] = "🎲", ["video_game"] = "🎮",
    };
}
