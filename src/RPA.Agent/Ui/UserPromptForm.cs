namespace RPA.Agent.Ui;

using System.Windows.Forms;
using RPA.Agent.Localization;
using RPA.Agent.Prompts;

/// <summary>
/// UserPrompt modal penceresi (Spec Bölüm 9): workflow yürütmesi bir kullanıcı girdisi bekliyorken
/// gösterilir. Girdi türüne göre metin kutusu / seçim listesi / onay düğmeleri sunar. Kullanıcı
/// Gönder'e bastığında <see cref="UserPromptService.Submit"/> çağrılır ve pencere kapanır.
/// </summary>
public sealed class UserPromptForm : Form
{
    private readonly UserPromptService _promptService;
    private readonly UserPromptRequest _request;
    private readonly TextBox? _textBox;
    private readonly ComboBox? _comboBox;

    public UserPromptForm(UserPromptService promptService, UserPromptRequest request, AgentLanguage language = AgentLanguage.Turkish)
    {
        _promptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        _request = request ?? throw new ArgumentNullException(nameof(request));

        Text = string.IsNullOrWhiteSpace(request.Title) ? AgentStrings.Get("UserPrompt.Title", language) : request.Title;
        Width = 420;
        Height = 220;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;

        var messageLabel = new Label { Text = request.Message, Dock = DockStyle.Top, Height = 60, Padding = new Padding(10) };
        Controls.Add(messageLabel);

        if (request.Kind == UserPromptKind.Text)
        {
            _textBox = new TextBox { Dock = DockStyle.Top };
            Controls.Add(_textBox);
        }
        else if (request.Kind == UserPromptKind.Choice)
        {
            _comboBox = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var choice in request.Choices)
                _comboBox.Items.Add(choice);
            if (_comboBox.Items.Count > 0)
                _comboBox.SelectedIndex = 0;
            Controls.Add(_comboBox);
        }

        var submitButton = new Button { Text = AgentStrings.Get("UserPrompt.Submit", language), Dock = DockStyle.Bottom };
        submitButton.Click += (_, _) => Submit(confirmed: true);
        Controls.Add(submitButton);

        var cancelButton = new Button { Text = AgentStrings.Get("UserPrompt.Cancel", language), Dock = DockStyle.Bottom };
        cancelButton.Click += (_, _) => Submit(confirmed: false);
        Controls.Add(cancelButton);
    }

    private void Submit(bool confirmed)
    {
        var response = new UserPromptResponse(
            _request.Id,
            confirmed,
            text: _textBox?.Text,
            selectedChoice: _comboBox?.SelectedItem as string);
        _promptService.Submit(response);
        Close();
    }
}
