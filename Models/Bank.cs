using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Models
{
    public class Bank
    {
        public string Code { get; set; }          // Код
        public string Name { get; set; }          // Наименование банка
        public string BIC { get; set; }           // БИК
        public string Branch { get; set; }        // Отделение
        public string Address { get; set; }       // Адрес
        public string Phone { get; set; }         // Телефон
        public string Swift { get; set; }         // SWIFT
        public string Chips { get; set; }         // CHIPS
        public string AddressEng { get; set; }    // Адрес на англ.
        public bool IsActive { get; set; }        // Активен
        public string Description { get; set; }   // Примечание
    }
}
