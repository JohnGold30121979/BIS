using System.Threading.Tasks;

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
        bool ShowRegister();
        bool ShowCreateInfoBase(out string? infoBaseName);
        void ShowSetup();
    }
}
