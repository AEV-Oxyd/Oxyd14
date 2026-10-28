namespace Content.Shared._Oxyd.Economy;

/// <summary>Eris wage ladder (credits per payday). See CEV-Eris code/__DEFINES/economy.dm.</summary>
public static class OxydEconomyConstants
{
    public const int WageNone = 0;
    public const int WageLabourDumb = 225;
    public const int WageLabour = 300;
    public const int WageLabourHazard = 375;
    public const int WageProfessional = 450;
    public const int WageCommand = 600;

    public const int PaydayIntervalMinutes = 30;
    public const int PersonalAccountCreateFee = 500;
    public const int DepartmentAccountCreateFee = 2000;

    public const int BudgetBase = 500;

    public const string EmployerService = "Service";
    public const string EmployerCargo = "Cargo";
    public const string EmployerGuild = "Guild";
    public const string EmployerSecurity = "Security";
    public const string EmployerEngineering = "Engineering";
    public const string EmployerMedical = "Medical";
    public const string EmployerScience = "Science";
}
