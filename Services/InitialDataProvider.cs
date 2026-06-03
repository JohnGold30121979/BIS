using System.Collections.Generic;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public static class InitialDataProvider
    {
        // План счетов бухгалтерского учета КР (основные счета)
        public static List<ChartOfAccount> GetChartOfAccounts()
        {
            return new List<ChartOfAccount>
            {
                // Раздел 1. Внеоборотные активы (100)
                new ChartOfAccount { Code = "101", Name = "Основные средства", AccountType = "Active", Level = 1, Order = 1,
                    Description = "Учет основных средств предприятия" },
                new ChartOfAccount { Code = "102", Name = "Нематериальные активы", AccountType = "Active", Level = 1, Order = 2,
                    Description = "Учет нематериальных активов" },
                new ChartOfAccount { Code = "103", Name = "Незавершенное строительство", AccountType = "Active", Level = 1, Order = 3,
                    Description = "Учет затрат на строительство" },
                new ChartOfAccount { Code = "104", Name = "Долгосрочные инвестиции", AccountType = "Active", Level = 1, Order = 4,
                    Description = "Учет долгосрочных финансовых вложений" },
                new ChartOfAccount { Code = "105", Name = "Амортизация основных средств", AccountType = "Passive", Level = 1, Order = 5,
                    Description = "Учет накопленной амортизации ОС" },
                new ChartOfAccount { Code = "106", Name = "Амортизация НМА", AccountType = "Passive", Level = 1, Order = 6,
                    Description = "Учет накопленной амортизации НМА" },

                // Раздел 2. Запасы (200)
                new ChartOfAccount { Code = "201", Name = "Сырье и материалы", AccountType = "Active", Level = 1, Order = 10,
                    Description = "Учет сырья и материалов" },
                new ChartOfAccount { Code = "202", Name = "Товары", AccountType = "Active", Level = 1, Order = 11,
                    Description = "Учет товаров для перепродажи" },
                new ChartOfAccount { Code = "203", Name = "Готовая продукция", AccountType = "Active", Level = 1, Order = 12,
                    Description = "Учет готовой продукции" },
                new ChartOfAccount { Code = "204", Name = "Незавершенное производство", AccountType = "Active", Level = 1, Order = 13,
                    Description = "Учет затрат в незавершенном производстве" },
                new ChartOfAccount { Code = "205", Name = "Тара и тарные материалы", AccountType = "Active", Level = 1, Order = 14,
                    Description = "Учет тары" },
                new ChartOfAccount { Code = "206", Name = "Резерв под снижение стоимости запасов", AccountType = "Passive", Level = 1, Order = 15,
                    Description = "Резерв под обесценение запасов" },

                // Раздел 3. Денежные средства (300)
                new ChartOfAccount { Code = "301", Name = "Касса в национальной валюте", AccountType = "Active", Level = 1, Order = 20,
                    Description = "Учет наличных денежных средств в кассе" },
                new ChartOfAccount { Code = "302", Name = "Касса в иностранной валюте", AccountType = "Active", Level = 1, Order = 21,
                    Description = "Учет наличных денежных средств в кассе (валюта)" },
                new ChartOfAccount { Code = "303", Name = "Расчетный счет", AccountType = "Active", Level = 1, Order = 22,
                    Description = "Учет денежных средств на расчетном счете" },
                new ChartOfAccount { Code = "304", Name = "Валютный счет", AccountType = "Active", Level = 1, Order = 23,
                    Description = "Учет денежных средств на валютном счете" },
                new ChartOfAccount { Code = "305", Name = "Денежные документы", AccountType = "Active", Level = 1, Order = 24,
                    Description = "Учет денежных документов" },
                new ChartOfAccount { Code = "306", Name = "Переводы в пути", AccountType = "Active", Level = 1, Order = 25,
                    Description = "Учет переводов в пути" },
                new ChartOfAccount { Code = "307", Name = "Специальные счета в банках", AccountType = "Active", Level = 1, Order = 26,
                    Description = "Учет средств на специальных счетах" },
                new ChartOfAccount { Code = "308", Name = "Депозиты", AccountType = "Active", Level = 1, Order = 27,
                    Description = "Учет депозитных вкладов" },

                // Раздел 4. Расчеты (400)
                new ChartOfAccount { Code = "401", Name = "Расчеты с поставщиками", AccountType = "ActivePassive", Level = 1, Order = 30,
                    Description = "Учет расчетов с поставщиками и подрядчиками" },
                new ChartOfAccount { Code = "402", Name = "Расчеты с покупателями", AccountType = "ActivePassive", Level = 1, Order = 31,
                    Description = "Учет расчетов с покупателями и заказчиками" },
                new ChartOfAccount { Code = "403", Name = "Расчеты по авансам выданным", AccountType = "Active", Level = 1, Order = 32,
                    Description = "Учет выданных авансов" },
                new ChartOfAccount { Code = "404", Name = "Расчеты по авансам полученным", AccountType = "Passive", Level = 1, Order = 33,
                    Description = "Учет полученных авансов" },
                new ChartOfAccount { Code = "405", Name = "Расчеты с подотчетными лицами", AccountType = "ActivePassive", Level = 1, Order = 34,
                    Description = "Учет расчетов с подотчетными лицами" },
                new ChartOfAccount { Code = "406", Name = "Расчеты с персоналом по оплате труда", AccountType = "Passive", Level = 1, Order = 35,
                    Description = "Учет заработной платы" },
                new ChartOfAccount { Code = "407", Name = "Расчеты с бюджетом", AccountType = "Passive", Level = 1, Order = 36,
                    Description = "Учет расчетов по налогам и сборам" },
                new ChartOfAccount { Code = "408", Name = "Расчеты по социальному страхованию", AccountType = "Passive", Level = 1, Order = 37,
                    Description = "Учет расчетов по соцстраху" },
                new ChartOfAccount { Code = "409", Name = "Расчеты с учредителями", AccountType = "ActivePassive", Level = 1, Order = 38,
                    Description = "Учет расчетов с учредителями" },
                new ChartOfAccount { Code = "410", Name = "Расчеты по кредитам и займам", AccountType = "Passive", Level = 1, Order = 39,
                    Description = "Учет кредитов и займов" },
                new ChartOfAccount { Code = "411", Name = "Расчеты с разными дебиторами", AccountType = "Active", Level = 1, Order = 40,
                    Description = "Учет прочих дебиторов" },
                new ChartOfAccount { Code = "412", Name = "Расчеты с разными кредиторами", AccountType = "Passive", Level = 1, Order = 41,
                    Description = "Учет прочих кредиторов" },
                new ChartOfAccount { Code = "413", Name = "Резерв по сомнительным долгам", AccountType = "Passive", Level = 1, Order = 42,
                    Description = "Резерв под обесценение дебиторской задолженности" },

                // Раздел 5. Капитал и резервы (500)
                new ChartOfAccount { Code = "501", Name = "Уставный капитал", AccountType = "Passive", Level = 1, Order = 50,
                    Description = "Учет уставного капитала" },
                new ChartOfAccount { Code = "502", Name = "Добавочный капитал", AccountType = "Passive", Level = 1, Order = 51,
                    Description = "Учет добавочного капитала" },
                new ChartOfAccount { Code = "503", Name = "Резервный капитал", AccountType = "Passive", Level = 1, Order = 52,
                    Description = "Учет резервного капитала" },
                new ChartOfAccount { Code = "504", Name = "Нераспределенная прибыль (непокрытый убыток)", AccountType = "Passive", Level = 1, Order = 53,
                    Description = "Учет нераспределенной прибыли" },
                new ChartOfAccount { Code = "505", Name = "Целевое финансирование", AccountType = "Passive", Level = 1, Order = 54,
                    Description = "Учет целевых средств" },

                // Раздел 6. Доходы (600)
                new ChartOfAccount { Code = "601", Name = "Доходы от реализации", AccountType = "Passive", Level = 1, Order = 60,
                    Description = "Учет доходов от основной деятельности" },
                new ChartOfAccount { Code = "602", Name = "Прочие доходы", AccountType = "Passive", Level = 1, Order = 61,
                    Description = "Учет прочих доходов" },
                new ChartOfAccount { Code = "603", Name = "Доходы от финансовой деятельности", AccountType = "Passive", Level = 1, Order = 62,
                    Description = "Учет доходов от финансовых операций" },

                // Раздел 7. Расходы (700)
                new ChartOfAccount { Code = "701", Name = "Себестоимость реализованной продукции", AccountType = "Active", Level = 1, Order = 70,
                    Description = "Учет себестоимости" },
                new ChartOfAccount { Code = "702", Name = "Расходы по реализации", AccountType = "Active", Level = 1, Order = 71,
                    Description = "Учет коммерческих расходов" },
                new ChartOfAccount { Code = "703", Name = "Общие и административные расходы", AccountType = "Active", Level = 1, Order = 72,
                    Description = "Учет управленческих расходов" },
                new ChartOfAccount { Code = "704", Name = "Прочие расходы", AccountType = "Active", Level = 1, Order = 73,
                    Description = "Учет прочих расходов" },
                new ChartOfAccount { Code = "705", Name = "Расходы по финансовой деятельности", AccountType = "Active", Level = 1, Order = 74,
                    Description = "Учет расходов от финансовых операций" },
                new ChartOfAccount { Code = "706", Name = "Расходы по налогам", AccountType = "Active", Level = 1, Order = 75,
                    Description = "Учет налоговых расходов" }
            };
        }

        // Банки Кыргызстана
        public static List<Bank> GetBanks()
        {
            return new List<Bank>
            {
                new Bank {
                    Name = "Открытое акционерное общество «Кыргызкоммерцбанк»",
                    ShortName = "Кыргызкоммерцбанк",
                    BIC = "105001",
                    INN = "02910198910019",
                    OKPO = "20137117",
                    Address = "г. Бишкек, ул. Шопокова, 101",
                    Phone = "+996 312 33 30 00",
                    Website = "www.kkb.kg",
                    Email = "bishkek@kkb.kg",
                    SwiftCode = "KAKYKG22XXX",
                    CorrespondentAccount = "1013810000520195",
                    Order = 1
                },
                new Bank {
                    Name = "Открытое акционерное общество «Мбанк»",
                    ShortName = "Мбанк",
                    BIC = "125001",
                    INN = "01204199910016",
                    OKPO = "22192566",
                    Address = "г. Бишкек, ул. Тоголок Молдо, 54а",
                    Phone = "+996 312 61 33 33",
                    Website = "www.mbank.kg",
                    Email = "contact@cbk.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 2
                },
                new Bank {
                    Name = "Открытое акционерное общество «Оптима Банк»",
                    ShortName = "Оптима Банк",
                    BIC = "109001",
                    INN = "00904199310033",
                    OKPO = "20030380",
                    Address = "г. Бишкек, пр. Чынгыза Айтматова, 95/1",
                    Phone = "+996 312 90 59 59",
                    Website = "www.optimabank.kg",
                    Email = "bank@optimabank.kg",
                    SwiftCode = "ENEJKG22",
                    CorrespondentAccount = "1013810000520195",
                    Order = 3
                },
                new Bank {
                    Name = "Открытое акционерное общество «Дос-Кредобанк»",
                    ShortName = "Дос-Кредобанк",
                    BIC = "121001",
                    INN = "02002199710092",
                    OKPO = "2165",
                    Address = "г. Бишкек, пр. Чуй, 92, 6 этаж",
                    Phone = "8686",
                    Website = "www.dcb.kg",
                    Email = "office@doscredobank.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "1013810000440171",
                    Order = 4
                },
                new Bank {
                    Name = "Открытое акционерное общество «РСК Банк»",
                    ShortName = "РСК Банк",
                    BIC = "129001",
                    INN = "02907199610193",
                    OKPO = "21573007",
                    Address = "г. Бишкек, ул. Московская, 80/1",
                    Phone = "+996 312 65 67 46",
                    Website = "www.rsk.kg",
                    Email = "info@rsk.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 5
                },
                new Bank {
                    Name = "Открытое акционерное общество «Керемет Банк»",
                    ShortName = "Керемет Банк",
                    BIC = "118001",
                    INN = "00712200610038",
                    OKPO = "21249734",
                    Address = "г. Бишкек, ул. Тоголок Молдо, 40/4",
                    Phone = "+996 312 31 31 73",
                    Website = "www.keremetbank.kg",
                    Email = "call-center@keremetbank.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 6
                },
                new Bank {
                    Name = "Открытое акционерное общество «БАКАЙ БАНК»",
                    ShortName = "БАКАЙ БАНК",
                    BIC = "106001",
                    INN = "00801199610029",
                    OKPO = "20319629",
                    Address = "г. Бишкек, ул. Мичурина, 56",
                    Phone = "+996 312 61 00 61",
                    Website = "www.bakai.kg",
                    Email = "bank@bakai.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 7
                },
                new Bank {
                    Name = "Открытое акционерное общество «Айыл Банк»",
                    ShortName = "Айыл Банк",
                    BIC = "133001",
                    INN = "02004200010117",
                    OKPO = "23665825",
                    Address = "г. Бишкек, ул. Логвиненко, 14",
                    Phone = "+996 312 66 52 78",
                    Website = "www.ab.kg",
                    Email = "office@ab.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 8
                },
                new Bank {
                    Name = "Открытое акционерное общество «Капитал Банк»",
                    ShortName = "Капитал Банк",
                    BIC = "113001",
                    INN = "01102199510086",
                    OKPO = "22763950",
                    Address = "г. Бишкек, ул. Московская, 161",
                    Phone = "+996 312 31 30 30",
                    Website = "www.capitalbank.kg",
                    Email = "office@capitalbank.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 9
                },
                new Bank {
                    Name = "Закрытое акционерное общество «Банк Азии»",
                    ShortName = "Банк Азии",
                    BIC = "112001",
                    INN = "01612199710025",
                    OKPO = "22749664",
                    Address = "г. Бишкек, пр. Ч.Айтматова, 303",
                    Phone = "+996 312 55 00 01",
                    Website = "www.bankasia.kg",
                    Email = "bankasia@bankasia.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 10
                },
                new Bank {
                    Name = "Закрытое акционерное общество «ФИНКА Банк»",
                    ShortName = "ФИНКА Банк",
                    BIC = "131001",
                    INN = "02008200210142",
                    OKPO = "24376284",
                    Address = "г. Бишкек, ул. Шопокова, 93/2",
                    Phone = "+996 312 44 04 40",
                    Website = "www.fincabank.kg",
                    Email = "finca@fincabank.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 11
                },
                new Bank {
                    Name = "Закрытое акционерное общество «Банк Компаньон»",
                    ShortName = "Банк Компаньон",
                    BIC = "117001",
                    INN = "02207200110050",
                    OKPO = "26120484",
                    Address = "г. Бишкек, ул. Шота Руставели, 62",
                    Phone = "+996 312 97 99 79",
                    Website = "www.kompanion.kg",
                    Email = "office@kompanion.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 12
                },
                new Bank {
                    Name = "Закрытое акционерное общество «Кыргызский Инвестиционно-Кредитный Банк»",
                    ShortName = "КИКБ",
                    BIC = "119001",
                    INN = "02207200110050",
                    OKPO = "22830344",
                    Address = "г. Бишкек, бульвар Эркиндик, 21",
                    Phone = "+996 312 62 01 01",
                    Website = "www.kicb.net",
                    Email = "reception@kicb.net",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 13
                },
                new Bank {
                    Name = "Закрытое акционерное общество «Демир Кыргыз Интернэшнл Банк»",
                    ShortName = "Демир Банк",
                    BIC = "118001",
                    INN = "01112199610073",
                    OKPO = "21634476",
                    Address = "г. Бишкек, пр. Чуй, 245",
                    Phone = "+996 312 61 06 10",
                    Website = "www.demirbank.kg",
                    Email = "customercare@demirbank.kg",
                    SwiftCode = "",
                    CorrespondentAccount = "",
                    Order = 14
                }
            };
        }
    }
}