namespace BoardEmpire.Core;

/// <summary>Tiny string table. Keys are the English text, so missing translations stay readable.</summary>
public static class Loc
{
    public static string Language { get; set; } = "en";

    public static readonly (string Code, string Name)[] Languages = { ("en", "English"), ("hi", "हिन्दी") };

    private static readonly Dictionary<string, string> Hindi = new()
    {
        ["Play"] = "खेलें",
        ["Solo"] = "अकेले",
        ["Local"] = "स्थानीय",
        ["Online"] = "ऑनलाइन",
        ["Settings"] = "सेटिंग्स",
        ["Profile"] = "प्रोफ़ाइल",
        ["Rules"] = "नियम",
        ["Replays"] = "रीप्ले",
        ["Quit"] = "बाहर निकलें",
        ["Back"] = "वापस",
        ["Continue match"] = "खेल जारी रखें",
        ["Pass & Play"] = "पास और खेलें",
        ["Same Wi-Fi"] = "एक ही वाई-फ़ाई",
        ["Nearby"] = "आस-पास",
        ["Quick Match"] = "त्वरित मैच",
        ["Ranked"] = "रैंक्ड",
        ["Private Room"] = "निजी कमरा",
        ["Join Code"] = "कोड से जुड़ें",
        ["Roll"] = "पासा फेंकें",
        ["End turn"] = "बारी समाप्त",
        ["Buy"] = "खरीदें",
        ["Auction"] = "नीलामी",
        ["Trade"] = "सौदा",
        ["Portfolio"] = "संपत्तियाँ",
        ["Build"] = "निर्माण",
        ["Sell"] = "बेचें",
        ["Mortgage"] = "गिरवी",
        ["Unmortgage"] = "गिरवी छुड़ाएँ",
        ["Pay fine"] = "जुर्माना भरें",
        ["Use card"] = "कार्ड लगाएँ",
        ["Pass"] = "छोड़ें",
        ["Bid"] = "बोली",
        ["Accept"] = "स्वीकार",
        ["Reject"] = "अस्वीकार",
        ["Counter"] = "जवाबी प्रस्ताव",
        ["Ready"] = "तैयार",
        ["Start match"] = "मैच शुरू करें",
        ["Add bot"] = "बॉट जोड़ें",
        ["Add player"] = "खिलाड़ी जोड़ें",
        ["Leave"] = "छोड़ें",
        ["Results"] = "नतीजे",
        ["Rematch"] = "दोबारा खेलें",
        ["Main menu"] = "मुख्य मेनू",
        ["Declare bankruptcy"] = "दिवालिया घोषित करें",
        ["Your turn"] = "आपकी बारी",
        ["Round"] = "दौर",
        ["Owner"] = "मालिक",
        ["Rent"] = "किराया",
        ["Price"] = "कीमत",
        ["Bank"] = "बैंक",
        ["Waiting for other players"] = "अन्य खिलाड़ियों की प्रतीक्षा",
        ["Language"] = "भाषा",
        ["Music"] = "संगीत",
        ["Sound effects"] = "ध्वनि प्रभाव",
        ["Haptics"] = "कंपन",
        ["View"] = "दृश्य",
        ["Animation speed"] = "ऐनिमेशन गति",
    };

    public static string T(string text) =>
        Language == "hi" && Hindi.TryGetValue(text, out var translated) ? translated : text;
}
