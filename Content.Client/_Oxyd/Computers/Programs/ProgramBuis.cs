using Content.Client._Oxyd.Computers.UI;

namespace Content.Client._Oxyd.Computers.Programs;

/// <summary>
/// Concrete BoundUserInterface types registered in YAML for every NTOS Program Entity.
/// </summary>
public sealed class OsProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<OsUiControl>(owner, uiKey);
public sealed class EmbeddedOsProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<EmbeddedOsUiControl>(owner, uiKey);
public sealed class TerminalProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<TerminalUiControl>(owner, uiKey);
public sealed class FileBrowserProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<FileBrowserUiControl>(owner, uiKey);
public sealed class DownloaderProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<DownloaderUiControl>(owner, uiKey);
public sealed class RouterProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<RouterUiControl>(owner, uiKey);
public sealed class PacketSnifferProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<PacketSnifferUiControl>(owner, uiKey);
public sealed class ApcControlProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<ApcControlUiControl>(owner, uiKey);
public sealed class AtmosControlProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<AtmosControlUiControl>(owner, uiKey);
public sealed class LatheControlProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<LatheControlUiControl>(owner, uiKey);
public sealed class NotekeeperProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<NotekeeperUiControl>(owner, uiKey);
public sealed class NanoTaskProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<NanoTaskUiControl>(owner, uiKey);
public sealed class CrewManifestProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<CrewManifestUiControl>(owner, uiKey);
public sealed class NewsReaderProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<NewsReaderUiControl>(owner, uiKey);
public sealed class WantedListProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<WantedListUiControl>(owner, uiKey);
public sealed class NetProbeProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<NetProbeUiControl>(owner, uiKey);
public sealed class LogProbeProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<LogProbeUiControl>(owner, uiKey);
public sealed class MedTekProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<UtilityProgramUiControl>(owner, uiKey);
public sealed class AstroNavProgramBui(EntityUid owner, Enum uiKey) : ProgramBoundUserInterface<UtilityProgramUiControl>(owner, uiKey);
