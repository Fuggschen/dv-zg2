using System;
using System.Collections.Generic;

namespace ZG2
{
    /// <summary>
    /// Names of the vanilla job types (and the header of the mod job type list) in all 26 game languages.
    /// Index: 0 = Freight Haul, 1 = Logistical Haul, 2 = Shunting Load, 3 = Shunting Unload,
    /// 4 = "Detected job types from mods".
    /// </summary>
    public static class ZG2JobTypeNames
    {
        public const int FreightHaul = 0, LogisticalHaul = 1, ShuntingLoad = 2, ShuntingUnload = 3, ModHeader = 4;

        private static readonly Dictionary<string, string[]> table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new[] { "Freight Haul", "Logistical Haul", "Shunting Load", "Shunting Unload", "Detected job types from mods" },
            ["de"] = new[] { "Güterverkehr", "Logistik (Leerwagen)", "Rangieren – Beladen", "Rangieren – Entladen", "Erkannte Jobtypen aus Mods" },
            ["bg"] = new[] { "Товарен превоз", "Логистичен превоз", "Маневриране – товарене", "Маневриране – разтоварване", "Открити типове задачи от модове" },
            ["zh-cn"] = new[] { "货运", "物流运输", "调车装货", "调车卸货", "从模组检测到的任务类型" },
            ["zh-tw"] = new[] { "貨運", "物流運輸", "調車裝貨", "調車卸貨", "從模組偵測到的任務類型" },
            ["cs"] = new[] { "Nákladní přeprava", "Logistická přeprava", "Posun – nakládka", "Posun – vykládka", "Rozpoznané typy zakázek z modů" },
            ["da"] = new[] { "Godstransport", "Logistiktransport", "Rangering – lastning", "Rangering – losning", "Fundne opgavetyper fra mods" },
            ["nl"] = new[] { "Goederenvervoer", "Logistiek vervoer", "Rangeren – laden", "Rangeren – lossen", "Gevonden opdrachttypen uit mods" },
            ["fi"] = new[] { "Rahtikuljetus", "Logistiikkakuljetus", "Vaihtotyö – lastaus", "Vaihtotyö – purku", "Modeista tunnistetut tehtävätyypit" },
            ["fr"] = new[] { "Transport de fret", "Transport logistique", "Manœuvre – chargement", "Manœuvre – déchargement", "Types de missions détectés (mods)" },
            ["hi"] = new[] { "माल ढुलाई", "लॉजिस्टिक ढुलाई", "शंटिंग – लदान", "शंटिंग – उतराई", "मॉड्स से पहचाने गए जॉब प्रकार" },
            ["hu"] = new[] { "Teherszállítás", "Logisztikai szállítás", "Tolatás – rakodás", "Tolatás – kirakodás", "Modokból felismert munkatípusok" },
            ["it"] = new[] { "Trasporto merci", "Trasporto logistico", "Manovra – carico", "Manovra – scarico", "Tipi di incarico rilevati dalle mod" },
            ["ja"] = new[] { "貨物輸送", "物流輸送", "入換（積込）", "入換（荷降ろし）", "MODから検出したジョブタイプ" },
            ["ko"] = new[] { "화물 운송", "물류 운송", "입환 – 적재", "입환 – 하역", "모드에서 감지된 작업 유형" },
            ["no"] = new[] { "Godstransport", "Logistikktransport", "Skifting – lasting", "Skifting – lossing", "Oppdagede oppdragstyper fra mods" },
            ["pl"] = new[] { "Przewóz towarów", "Przewóz logistyczny", "Manewry – załadunek", "Manewry – rozładunek", "Wykryte typy zleceń z modów" },
            ["pt"] = new[] { "Transporte de mercadorias", "Transporte logístico", "Manobras – carga", "Manobras – descarga", "Tipos de trabalho detetados em mods" },
            ["pt-br"] = new[] { "Transporte de carga", "Transporte logístico", "Manobra – carregamento", "Manobra – descarregamento", "Tipos de trabalho detectados em mods" },
            ["ro"] = new[] { "Transport de marfă", "Transport logistic", "Manevră – încărcare", "Manevră – descărcare", "Tipuri de sarcini detectate din moduri" },
            ["ru"] = new[] { "Грузовая перевозка", "Логистическая перевозка", "Маневры – погрузка", "Маневры – разгрузка", "Обнаруженные типы заданий из модов" },
            ["sk"] = new[] { "Nákladná preprava", "Logistická preprava", "Posun – nakládka", "Posun – vykládka", "Rozpoznané typy zákaziek z modov" },
            ["es"] = new[] { "Transporte de mercancías", "Transporte logístico", "Maniobras – carga", "Maniobras – descarga", "Tipos de trabajo detectados en mods" },
            ["sv"] = new[] { "Godstransport", "Logistiktransport", "Växling – lastning", "Växling – lossning", "Upptäckta uppdragstyper från moddar" },
            ["tr"] = new[] { "Yük taşımacılığı", "Lojistik taşıma", "Manevra – yükleme", "Manevra – boşaltma", "Modlardan algılanan iş türleri" },
            ["uk"] = new[] { "Вантажне перевезення", "Логістичне перевезення", "Маневри – навантаження", "Маневри – розвантаження", "Виявлені типи завдань із модів" },
        };

        public static string Get(int index)
        {
            if (!table.TryGetValue(ZG2Loc.LanguageCode, out var row)) row = table["en"];
            return index >= 0 && index < row.Length ? row[index] : string.Empty;
        }
    }
}
