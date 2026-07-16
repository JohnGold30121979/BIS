using System.Threading.Tasks;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public enum DialogResultKind
    {
        None,
        Ok,
        Cancel,
        Yes,
        No
    }

    public interface IDialogService
    {
        void ShowInformation(string message, string title = "Информация");
        void ShowWarning(string message, string title = "Внимание");
        void ShowError(string message, string title = "Ошибка");
        bool Confirm(string message, string title = "Подтверждение");
        Task<bool> ShowLoginAsync();
        bool ShowCreateInfoBase(out string? infoBaseName);
        bool ShowEditInfoBase(InfoBase infoBase, out string? infoBaseName, out string? infoBaseIcon);
        void ShowSetup();
    }
}
