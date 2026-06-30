using System.Collections.Generic;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public static class InitialDataProvider
    {
        // План счетов бухгалтерского учета КР (основные счета)
        public static List<ChartOfAccount> GetChartOfAccounts()
        {
            var accounts = new List<ChartOfAccount>();

            // Контрольные корреспондирующие счета для автопроверки проводок.
            accounts.Add(new ChartOfAccount { Code = "4010", Name = "Расчеты с поставщиками", AccountType = "Passive", Description = "Корреспондирующий счет для оплаты поставщикам", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "6010", Name = "Доходы от реализации", AccountType = "Passive", Description = "Корреспондирующий счет для поступления оплаты от покупателей", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "6810", Name = "Расчеты с персоналом по оплате труда", AccountType = "Passive", Description = "Корреспондирующий счет для выплаты заработной платы", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "6850", Name = "Расчеты с подотчетными лицами", AccountType = "Active", Description = "Корреспондирующий счет для выдачи денежных средств под отчет", Level = 1 });

            // ==================== РАЗДЕЛ 1000: ОБОРОТНЫЕ АКТИВЫ ====================
            accounts.Add(new ChartOfAccount { Code = "11100000", Name = "Денежные средства в национальной валюте", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11200000", Name = "Денежные средства в иностранной валюте", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11300000", Name = "Денежные документы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11600000", Name = "Денежные средства в терминалах ПО", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "12100000", Name = "Счета в национальной валюте", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "12200000", Name = "Счета в иностранной валюте в местных банках", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "12300000", Name = "Счета в зарубежных банках", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "12400000", Name = "Денежные средства в банках, ограниченные к использованию", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "12500000", Name = "Денежные средства в пути", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "13300000", Name = "Займы выданные", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "13400000", Name = "Депозитные вклады в национальной валюте", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "13600000", Name = "Депозитные вклады в иностранной валюте", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "13700000", Name = "Краткосрочные инвестиции в дочерние компании", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "13900000", Name = "Прочие краткосрочные инвестиции", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "14100000", Name = "Счета к получению за товары и услуги", AccountType = "ActivePassive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "14910000", Name = "Резерв на безнадежные долги по счетам к получению", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15200000", Name = "Дебиторская задолженность сотрудников и директоров", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15300000", Name = "Налоги, оплаченные авансом", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15400000", Name = "Налоги, подлежащие возмещению", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15500000", Name = "Проценты к получению", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15600000", Name = "Дивиденды к получению", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15700000", Name = "Дебиторская задолженность агентов", AccountType = "ActivePassive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15800000", Name = "Текущая часть долгосрочной дебиторской задолженности", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "15900000", Name = "Прочая дебиторская задолженность", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "16100000", Name = "Товары", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "16910000", Name = "Нереализованная торговая наценка", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "16200000", Name = "Запасы сырья и основных материалов", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "16300000", Name = "Незавершенное производство", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "16400000", Name = "Готовая продукция", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "17100000", Name = "Топливо", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "17200000", Name = "Запасные части", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "17400000", Name = "Прочие материалы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "17500000", Name = "Малоценные и быстроизнашивающиеся предметы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "17950000", Name = "МБП в эксплуатации", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "18100000", Name = "Запасы, оплаченные авансом", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "18200000", Name = "Услуги, оплаченные авансом", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "18300000", Name = "Аренда, оплаченная авансом", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "18400000", Name = "Предоплаты поставщикам товаров/услуг", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "18900000", Name = "Прочие виды авансированных платежей", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "19200000", Name = "Задолженность лиц, подписавшихся на акции второй эмиссии", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "19300000", Name = "Задолженность покупателей при продаже выкупленных акций", AccountType = "Active", Level = 1 });

            // ==================== РАЗДЕЛ 2000: ВНЕОБОРОТНЫЕ АКТИВЫ ====================
            accounts.Add(new ChartOfAccount { Code = "21100000", Name = "Земля", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21200000", Name = "Право пользования арендованным ОС", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21920000", Name = "Накопленная амортизация - право пользования", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21300000", Name = "Здания, сооружения", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21930000", Name = "Накопленная амортизация – здания", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21400000", Name = "Оборудование", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21940000", Name = "Накопленная амортизация – оборудование", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21500000", Name = "Конторское оборудование", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21950000", Name = "Накопленная амортизация – конторское", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21600000", Name = "Мебель и принадлежности", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21960000", Name = "Накопленная амортизация – мебель", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21700000", Name = "Транспортные средства", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21970000", Name = "Накопленная амортизация – транспорт", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21800000", Name = "Благоустройство арендованной собственности", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21980000", Name = "Накопленная амортизация – благоустройство аренды", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21900000", Name = "Благоустройство земельных участков", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21990000", Name = "Накопленная амортизация – благоустройство земли", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "21910000", Name = "Незавершенное строительство", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "24000000", Name = "Отсроченные налоговые требования", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "25000000", Name = "Денежные средства, ограниченные к использованию", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "27200000", Name = "Долгосрочная дебиторская задолженность покупателей", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "27800000", Name = "Долгосрочные отсроченные расходы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "27900000", Name = "Прочая долгосрочная дебиторская задолженность", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "28200000", Name = "Долгосрочные предоставленные займы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "28300000", Name = "Долгосрочные инвестиции в дочерние компании", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "28900000", Name = "Прочие долгосрочные инвестиции", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29100000", Name = "Франшиза", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29910000", Name = "Накопленная амортизация – франшиза", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29300000", Name = "Патенты", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29930000", Name = "Накопленная амортизация – патенты", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29400000", Name = "Торговые марки", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29940000", Name = "Накопленная амортизация – торговые марки", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29500000", Name = "Авторские права", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29950000", Name = "Накопленная амортизация – авторские права", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29600000", Name = "Программное обеспечение", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29960000", Name = "Накопленная амортизация – ПО", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29700000", Name = "Лицензионное соглашение", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29970000", Name = "Накопленная амортизация – лицензия", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29800000", Name = "Прочие активы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29980000", Name = "Накопленная амортизация – прочие активы", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "29900000", Name = "Незавершенные разработки", AccountType = "Active", Level = 1 });

            // ==================== РАЗДЕЛ 3000: КРАТКОСРОЧНЫЕ ОБЯЗАТЕЛЬСТВА ====================
            accounts.Add(new ChartOfAccount { Code = "31100000", Name = "Счета к оплате за товары и услуги по хоз.деятельности", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "31900000", Name = "Прочие счета к оплате", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "32100000", Name = "Авансы покупателей и заказчиков", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "32200000", Name = "Авансовые платежи агентов ПО", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "33100000", Name = "Банковские кредиты, займы", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "33200000", Name = "Прочие кредиты, займы", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "33300000", Name = "Текущая часть долгосрочных долговых обязательств", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "33900000", Name = "Прочие краткосрочные долговые обязательства", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "34100000", Name = "Налог на прибыль к оплате", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "34200000", Name = "Подоходный налог на доходы физ.лиц", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "34300000", Name = "НДС к оплате", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "34900000", Name = "Прочие налоги к оплате", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "35100000", Name = "Начисленные обязательства по оплате товаров и услуг", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "35200000", Name = "Начисленная заработная плата", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "35300000", Name = "Начисленные взносы на социальное страхование", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "35400000", Name = "Дивиденды к выплате", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "35500000", Name = "Начисленные проценты по долговым обязательствам", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "35900000", Name = "Прочие начисленные расходы", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "36100000", Name = "Счета к оплате за принятые платежи в пользу поставщиков", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "37100000", Name = "Резерв на гарантийное обслуживание", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "37200000", Name = "Резерв на оплату судебных исков", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "37300000", Name = "Прочие резервы", AccountType = "Passive", Level = 1 });

            // ==================== РАЗДЕЛ 4000: ДОЛГОСРОЧНЫЕ ОБЯЗАТЕЛЬСТВА ====================
            accounts.Add(new ChartOfAccount { Code = "41200000", Name = "Банковские кредиты, займы (долгосрочные)", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "41300000", Name = "Прочие кредиты, займы (долгосрочные)", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "41500000", Name = "Обязательства по аренде", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "41900000", Name = "Прочие долгосрочные обязательства", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "42000000", Name = "Отсроченные доходы", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "43000000", Name = "Отсроченные налоговые обязательства", AccountType = "Passive", Level = 1 });

            // ==================== РАЗДЕЛ 5000: СОБСТВЕННЫЙ КАПИТАЛ ====================
            accounts.Add(new ChartOfAccount { Code = "51100000", Name = "Простые акции", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "51200000", Name = "Привилегированные акции", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "51300000", Name = "Прочий уставный капитал", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "51910000", Name = "Выкупленные собственные акции", AccountType = "Passive", Level = 1 }); // контрпассив
            accounts.Add(new ChartOfAccount { Code = "52100000", Name = "Дополнительно оплаченный капитал", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "52400000", Name = "Капитал, авансированный собственником", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "53000000", Name = "Нераспределенная прибыль (убыток) прошлых лет", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "54000000", Name = "Резервный капитал", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "55000000", Name = "Прибыль (убытки) последнего отчетного года", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "59990000", Name = "Свод доходов и расходов", AccountType = "Passive", Level = 1 });

            // ==================== РАЗДЕЛ 6000: ДОХОДЫ ОТ ОПЕРАЦИОННОЙ ДЕЯТЕЛЬНОСТИ ====================
            accounts.Add(new ChartOfAccount { Code = "61100000", Name = "Выручка от реализации товаров и услуг", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "61200000", Name = "Возврат проданных товаров и скидки", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "61300000", Name = "Выручка от обмена товаров и услуг", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "61500000", Name = "Выручка от использования активов", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "61600000", Name = "Выручка от процессинга и клиринга", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "61700000", Name = "Выручка от технической поддержки", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "62100000", Name = "Доход от аренды", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "62500000", Name = "Возврат списанных безнадежных долгов", AccountType = "Passive", Level = 1 });

            // ==================== РАЗДЕЛ 7000: ОПЕРАЦИОННЫЕ РАСХОДЫ (прямые и косвенные) ====================
            // Прямые расходы (7100)
            accounts.Add(new ChartOfAccount { Code = "71100000", Name = "Затраты на приобретение товаров, сырья, материалов (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71200000", Name = "Затраты по оплате труда и соц.отчислениям (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71300000", Name = "Финансовые/процентные расходы по аренде (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71400000", Name = "Затраты на коммунальные услуги, связь (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71500000", Name = "Амортизация ОС (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71600000", Name = "Ремонт и обслуживание ОС, техподдержка (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71700000", Name = "Использование запасов для собственных нужд (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71800000", Name = "Амортизация НМА (прямые)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "71900000", Name = "Корректировки стоимости запасов (прямые)", AccountType = "Active", Level = 1 });
            // Косвенные расходы (7200)
            accounts.Add(new ChartOfAccount { Code = "72100000", Name = "Затраты на приобретение (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72200000", Name = "Затраты по оплате труда и соц.отчислениям (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72300000", Name = "Финансовые/процентные расходы по аренде (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72400000", Name = "Затраты на коммунальные услуги, связь (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72500000", Name = "Амортизация ОС (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72600000", Name = "Ремонт и обслуживание ОС, техподдержка (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72700000", Name = "Использование запасов для собственных нужд (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72800000", Name = "Амортизация НМА (косвенные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "72900000", Name = "Корректировки стоимости запасов (косвенные)", AccountType = "Active", Level = 1 });
            // Расходы, связанные с реализацией (7500)
            accounts.Add(new ChartOfAccount { Code = "75100000", Name = "Расходы на рекламу и содействие продаже", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75200000", Name = "Расходы по оплате труда (реализация)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75300000", Name = "Расходы по соц.отчислениям (реализация)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75400000", Name = "Расходы по хранению и транспорту", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75500000", Name = "Расходы по безнадежным долгам (реализация)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75600000", Name = "Расходы по гарантийному обслуживанию", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75700000", Name = "Прочие торговые издержки", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75800000", Name = "Амортизация ОС (реализация)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "75900000", Name = "Расходы на премиальные продажи", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "76000000", Name = "Прочие производственные расходы", AccountType = "Active", Level = 1 });

            // ==================== РАЗДЕЛ 8000: ОБЩИЕ И АДМИНИСТРАТИВНЫЕ РАСХОДЫ ====================
            accounts.Add(new ChartOfAccount { Code = "80100000", Name = "Расходы по оплате труда (административные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80200000", Name = "Расходы по соц.отчислениям (административные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80300000", Name = "Расходы по оплате аренды", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80400000", Name = "Расходы по оплате услуг", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80500000", Name = "Налог на имущество", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80600000", Name = "Расходы на канцелярские принадлежности", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80700000", Name = "Расходы на коммуникации", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80800000", Name = "Расходы по оплате страховок", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "80900000", Name = "Расходы по приобретению лицензий", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81000000", Name = "Расходы по НДС, не принимаемому к зачету", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81100000", Name = "Ремонт и тех.обслуживание ОС (административные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81200000", Name = "Расходы по компьютерному обеспечению", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81300000", Name = "Представительские расходы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81400000", Name = "Вознаграждение аудиторам", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81500000", Name = "Вознаграждение юристам", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81600000", Name = "Расходы по обучению", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81700000", Name = "Расходы по консультациям", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81800000", Name = "Расходы по связям с общественностью", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "81900000", Name = "Расходы по прочим налогам", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "82000000", Name = "Командировочные расходы (местные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "82100000", Name = "Командировочные расходы (международные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "82200000", Name = "Расходы по коммунальным услугам", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "82300000", Name = "Штрафы, пени, неустойки в бюджет", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "82400000", Name = "Штрафы, пени, неустойки по хоз.договорам", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "84700000", Name = "Амортизация ОС (административные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "84800000", Name = "Амортизация НМА (административные)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "84900000", Name = "Прочие общие и административные расходы", AccountType = "Active", Level = 1 });

            // ==================== РАЗДЕЛ 9000: ДОХОДЫ И РАСХОДЫ ОТ НЕОПЕРАЦИОННОЙ ДЕЯТЕЛЬНОСТИ ====================
            accounts.Add(new ChartOfAccount { Code = "91100000", Name = "Доход в виде процентов", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "91200000", Name = "Доход от ассоциированных, дочерних компаний", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "91300000", Name = "Доход от дивидендов", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "91400000", Name = "Доход от курсовых разниц по операциям в ин.валюте", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "91900000", Name = "Прочие неоперационные доходы", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "95100000", Name = "Расходы в виде процентов", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "95200000", Name = "Убытки от курсовых разниц по операциям в ин.валюте", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "95300000", Name = "Расходы по безнадежным долгам", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "95900000", Name = "Прочие неоперационные расходы", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "99100000", Name = "Расходы (доходы) по налогу на прибыль", AccountType = "Active", Level = 1 });

            // ==================== ЗАБАЛАНСОВЫЕ СЧЕТА ====================
            accounts.Add(new ChartOfAccount { Code = "11010000", Name = "Арендованные основные средства", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11011000", Name = "Амортизация арендованных основных средств", AccountType = "Passive", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11020000", Name = "МБП (забаланс)", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11030000", Name = "Бланки строгой отчетности", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11040000", Name = "Товары на хранении", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11050000", Name = "ТМЦ, принятые на ответственное хранение", AccountType = "Active", Level = 1 });
            accounts.Add(new ChartOfAccount { Code = "11060000", Name = "Списанная задолженность неплатежеспособных дебиторов", AccountType = "Active", Level = 1 });

            return accounts;
        }

        // Банки Кыргызстана
        public static List<Bank> GetBanks()
        {
            return new List<Bank>
    {
        new Bank {
            Code = "1",
            Name = "Открытое акционерное общество «Кыргызкоммерцбанк»",
            BIC = "105001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Шопокова, 101",
            Phone = "+996 312 33 30 00",
            Swift = "KAKYKG22XXX",
            Chips = "",
            AddressEng = "101 Shopokova str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "2",
            Name = "Открытое акционерное общество «Мбанк»",
            BIC = "125001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Тоголок Молдо, 54а",
            Phone = "+996 312 61 33 33",
            Swift = "",
            Chips = "",
            AddressEng = "54a Togolok Moldo str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "3",
            Name = "Открытое акционерное общество «Оптима Банк»",
            BIC = "109001",
            Branch = "Головной офис",
            Address = "г. Бишкек, пр. Чынгыза Айтматова, 95/1",
            Phone = "+996 312 90 59 59",
            Swift = "ENEJKG22",
            Chips = "",
            AddressEng = "95/1 Chyngyz Aitmatov ave., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "4",
            Name = "Открытое акционерное общество «Дос-Кредобанк»",
            BIC = "121001",
            Branch = "Головной офис",
            Address = "г. Бишкек, пр. Чуй, 92, 6 этаж",
            Phone = "8686",
            Swift = "",
            Chips = "",
            AddressEng = "92 Chuy ave., 6th floor, Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "5",
            Name = "Открытое акционерное общество «РСК Банк»",
            BIC = "129001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Московская, 80/1",
            Phone = "+996 312 65 67 46",
            Swift = "",
            Chips = "",
            AddressEng = "80/1 Moskovskaya str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "6",
            Name = "Открытое акционерное общество «Керемет Банк»",
            BIC = "118001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Тоголок Молдо, 40/4",
            Phone = "+996 312 31 31 73",
            Swift = "",
            Chips = "",
            AddressEng = "40/4 Togolok Moldo str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "7",
            Name = "Открытое акционерное общество «БАКАЙ БАНК»",
            BIC = "106001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Мичурина, 56",
            Phone = "+996 312 61 00 61",
            Swift = "",
            Chips = "",
            AddressEng = "56 Michurina str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "8",
            Name = "Открытое акционерное общество «Айыл Банк»",
            BIC = "133001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Логвиненко, 14",
            Phone = "+996 312 66 52 78",
            Swift = "",
            Chips = "",
            AddressEng = "14 Logvinenko str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "9",
            Name = "Открытое акционерное общество «Капитал Банк»",
            BIC = "113001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Московская, 161",
            Phone = "+996 312 31 30 30",
            Swift = "",
            Chips = "",
            AddressEng = "161 Moskovskaya str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "10",
            Name = "Закрытое акционерное общество «Банк Азии»",
            BIC = "112001",
            Branch = "Головной офис",
            Address = "г. Бишкек, пр. Ч.Айтматова, 303",
            Phone = "+996 312 55 00 01",
            Swift = "",
            Chips = "",
            AddressEng = "303 Ch.Aitmatov ave., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "11",
            Name = "Закрытое акционерное общество «ФИНКА Банк»",
            BIC = "131001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Шопокова, 93/2",
            Phone = "+996 312 44 04 40",
            Swift = "",
            Chips = "",
            AddressEng = "93/2 Shopokova str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "12",
            Name = "Закрытое акционерное общество «Банк Компаньон»",
            BIC = "117001",
            Branch = "Головной офис",
            Address = "г. Бишкек, ул. Шота Руставели, 62",
            Phone = "+996 312 97 99 79",
            Swift = "",
            Chips = "",
            AddressEng = "62 Shota Rustaveli str., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "13",
            Name = "Закрытое акционерное общество «Кыргызский Инвестиционно-Кредитный Банк»",
            BIC = "119001",
            Branch = "Головной офис",
            Address = "г. Бишкек, бульвар Эркиндик, 21",
            Phone = "+996 312 62 01 01",
            Swift = "",
            Chips = "",
            AddressEng = "21 Erkindik blvd., Bishkek",
            IsActive = true,
            Description = ""
        },
        new Bank {
            Code = "14",
            Name = "Закрытое акционерное общество «Демир Кыргыз Интернэшнл Банк»",
            BIC = "118001",
            Branch = "Головной офис",
            Address = "г. Бишкек, пр. Чуй, 245",
            Phone = "+996 312 61 06 10",
            Swift = "",
            Chips = "",
            AddressEng = "245 Chuy ave., Bishkek",
            IsActive = true,
            Description = ""
        }
    };
        }
    }
}
