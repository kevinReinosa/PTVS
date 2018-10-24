// Python Tools for Visual Studio
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using Microsoft.PythonTools.Analysis;
using Microsoft.PythonTools.Commands;
using Microsoft.PythonTools.Debugger;
using Microsoft.PythonTools.Debugger.DebugEngine;
using Microsoft.PythonTools.Debugger.Remote;
using Microsoft.PythonTools.Infrastructure;
using Microsoft.PythonTools.Intellisense;
using Microsoft.PythonTools.Logging;
using Microsoft.PythonTools.Interpreter;
using Microsoft.PythonTools.InterpreterList;
using Microsoft.PythonTools.Navigation;
using Microsoft.PythonTools.Options;
using Microsoft.PythonTools.Project;
using Microsoft.PythonTools.Repl;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.InteractiveWindow.Shell;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Navigation;
using Microsoft.VisualStudioTools.Project;
using NativeMethods = Microsoft.VisualStudioTools.Project.NativeMethods;
using Task = System.Threading.Tasks.Task;
using Microsoft.PythonTools.Environments;
using Microsoft.PythonTools.Common.Infrastructure;
using Microsoft.PythonTools.Infrastructure.Commands;

namespace Microsoft.PythonTools {
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>    
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]       // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is a package.
    // This attribute is used to register the informations needed to show the this package in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", AssemblyVersionInfo.Version, IconResourceID = 400)]

    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideKeyBindingTable(PythonConstants.EditorFactoryGuid, 3004, AllowNavKeyBinding = true)]
    [Description("Python Tools Package")]
    [ProvideAutomationObject("VsPython")]
    [ProvideLanguageEditorOptionPage(typeof(PythonAdvancedEditorOptionsPage), PythonConstants.LanguageName, "", "Advanced", "#113")]
    [ProvideLanguageEditorOptionPage(typeof(PythonFormattingGeneralOptionsPage), PythonConstants.LanguageName, "", "Formatting", "#126")]
    [ProvideLanguageEditorOptionPage(typeof(PythonFormattingGeneralOptionsPage), PythonConstants.LanguageName, "Formatting", "General", "#120")]
    //[ProvideLanguageEditorOptionPage(typeof(PythonFormattingNewLinesOptionsPage), PythonConstants.LanguageName, "Formatting", "New Lines", "#121")]
    [ProvideLanguageEditorOptionPage(typeof(PythonFormattingSpacingOptionsPage), PythonConstants.LanguageName, "Formatting", "Spacing", "#122")]
    [ProvideLanguageEditorOptionPage(typeof(PythonFormattingStatementsOptionsPage), PythonConstants.LanguageName, "Formatting", "Statements", "#123")]
    [ProvideLanguageEditorOptionPage(typeof(PythonFormattingWrappingOptionsPage), PythonConstants.LanguageName, "Formatting", "Wrapping", "#124")]
    [ProvideOptionPage(typeof(PythonInteractiveOptionsPage), "Python Tools", "Interactive Windows", 115, 117, true)]
    [ProvideOptionPage(typeof(PythonGeneralOptionsPage), "Python Tools", "General", 115, 120, true)]
    [ProvideOptionPage(typeof(PythonDiagnosticsOptionsPage), "Python Tools", "Diagnostics", 115, 129, true)]
    [ProvideOptionPage(typeof(PythonDebuggingOptionsPage), "Python Tools", "Debugging", 115, 125, true)]
    [ProvideOptionPage(typeof(PythonCondaOptionsPage), "Python Tools", "Conda", 115, 132, true)]
    [ProvideOptionPage(typeof(LanguageServerOptionsPage), "Python Tools", "Language Server", 115, 131, false)]
    [Guid(GuidList.guidPythonToolsPkgString)]              // our packages GUID        
    [ProvideLanguageService(typeof(PythonLanguageInfo), PythonConstants.LanguageName, 106, RequestStockColors = true, ShowSmartIndent = true, ShowCompletion = true, DefaultToInsertSpaces = true, HideAdvancedMembersByDefault = true, EnableAdvancedMembersOption = true, ShowDropDownOptions = true)]
    [ProvideLanguageExtension(typeof(PythonLanguageInfo), PythonConstants.FileExtension)]
    [ProvideLanguageExtension(typeof(PythonLanguageInfo), PythonConstants.WindowsFileExtension)]
    [ProvideLanguageExtension(typeof(PythonLanguageInfo), PythonConstants.StubFileExtension)]
    [ProvideDebugEngine(AD7Engine.DebugEngineName, typeof(AD7ProgramProvider), typeof(AD7Engine), AD7Engine.DebugEngineId, hitCountBp: true)]
    [ProvideDebugAdapter("VSCode Python Debugger", DebugAdapterLauncher.VSCodeDebugEngineId, DebugAdapterLauncher.DebugAdapterLauncherCLSID, CustomDebugAdapterProtocolExtension.CustomProtocolExtensionCLSID, "Python", "{DA3C7D59-F9E4-4697-BEE7-3A0703AF6BFF}", typeof(DebugAdapterLauncher), typeof(CustomDebugAdapterProtocolExtension))]
    [ProvideDebugLanguage("Python", "{DA3C7D59-F9E4-4697-BEE7-3A0703AF6BFF}", PythonExpressionEvaluatorGuid, AD7Engine.DebugEngineId)]
    [ProvideDebugPortSupplier("Python remote (ptvsd)", typeof(PythonRemoteDebugPortSupplier), PythonRemoteDebugPortSupplier.PortSupplierId, typeof(PythonRemoteDebugPortPicker))]
    [ProvideDebugPortPicker(typeof(PythonRemoteDebugPortPicker))]
    #region Exception List
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "ArithmeticError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "AssertionError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "AttributeError", BreakByDefault = false)]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "BaseException")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "BufferError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "BytesWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "DeprecationWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "EOFError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "EnvironmentError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "Exception")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "FloatingPointError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "FutureWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "GeneratorExit", BreakByDefault = false)]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "IOError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "ImportError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "ImportWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "IndentationError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "IndexError", BreakByDefault = false)]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "KeyError", BreakByDefault = false)]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "KeyboardInterrupt")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "LookupError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "MemoryError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "NameError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "NotImplementedError")]

    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "BlockingIOError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ChildProcessError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ConnectionError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ConnectionError", "BrokenPipeError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ConnectionError", "ConnectionAbortedError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ConnectionError", "ConnectionRefusedError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ConnectionError", "ConnectionResetError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "FileExistsError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "FileNotFoundError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "InterruptedError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "IsADirectoryError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "NotADirectoryError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "PermissionError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "ProcessLookupError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OSError", "TimeoutError")]

    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "OverflowError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "PendingDeprecationWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "ReferenceError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "RuntimeError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "RuntimeWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "StandardError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "StopIteration", BreakByDefault = false)]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "SyntaxError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "SyntaxWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "SystemError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "SystemExit")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "TabError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "TypeError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UnboundLocalError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UnicodeDecodeError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UnicodeEncodeError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UnicodeError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UnicodeTranslateError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UnicodeWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "UserWarning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "ValueError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "Warning")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "WindowsError")]
    [ProvideDebugException(AD7Engine.DebugEngineId, "Python Exceptions", "ZeroDivisionError")]
    #endregion
    [ProvideComponentPickerPropertyPage(typeof(PythonToolsPackage), typeof(WebPiComponentPickerControl), "WebPi", DefaultPageNameValue = "#4000")]
    [ProvideToolWindow(typeof(InterpreterListToolWindow), Style = VsDockStyle.Linked, Window = ToolWindowGuids80.SolutionExplorer)]
    [ProvideDiffSupportedContentType(PythonConstants.SourceFileExtensions, "")]
    [ProvidePeekSupportedContentType(PythonConstants.SourceFileExtensions, "")]
    [ProvideCodeExpansions(GuidList.guidPythonLanguageService, false, 106, "Python", @"Snippets\%LCID%\SnippetsIndex.xml", @"Snippets\%LCID%\Python\")]
    [ProvideCodeExpansionPath("Python", "Test", @"Snippets\%LCID%\Test\")]
    [ProvideInteractiveWindow(GuidList.guidPythonInteractiveWindow, Style = VsDockStyle.Linked, Orientation = ToolWindowOrientation.none, Window = ToolWindowGuids80.Outputwindow)]
    [ProvideBraceCompletion(PythonCoreConstants.ContentType)]
    [ProvideFeatureFlag("Python.Analyzer.LiveLinting", false)]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "Object is owned by VS and cannot be disposed")]
    internal sealed class PythonToolsPackage : CommonPackage, IVsComponentSelectorProvider, IPythonToolsToolWindowService {
        private PythonAutomation _autoObject;
        private PackageContainer _packageContainer;
        private VsStatusBar _statusBar;
        private EnvironmentSwitcherStatusBar _environmentSwitcherStatusBar;
        private DisposableBag _disposables;
        internal const string PythonExpressionEvaluatorGuid = "{D67D5DB8-3D44-4105-B4B8-47AB1BA66180}";

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public PythonToolsPackage() {
            _disposables = new DisposableBag(GetType().Name, "Package is disposed");

            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));

#if DEBUG
            TaskScheduler.UnobservedTaskException += (sender, e) => {
                if (!e.Observed) {
                    var str = e.Exception.ToString();
                    if (str.Contains("Python")) {
                        try {
                            ActivityLog.LogError(
                                "UnobservedTaskException",
                                string.Format("An exception in a task was not observed: {0}", e.Exception.ToString())
                            );
                        } catch (InvalidOperationException) {
                        }
                        Debug.Fail("An exception in a task was not observed. See ActivityLog.xml for more details.", e.Exception.ToString());
                    }
                    e.SetObserved();
                }
            };
#endif
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            if (disposing) {
                _disposables.TryDispose();
            }
        }

        public override IVsAsyncToolWindowFactory GetAsyncToolWindowFactory(Guid toolWindowType)
            => toolWindowType == typeof(InterpreterListToolWindow).GUID ? this : base.GetAsyncToolWindowFactory(toolWindowType);

        protected override Task<object> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken)
            => toolWindowType == typeof(InterpreterListToolWindow) ? Task.FromResult<object>(this) : base.InitializeToolWindowAsync(toolWindowType, id, cancellationToken);

        protected override int CreateToolWindow(ref Guid toolWindowType, int id) {
            // We can't move initialization of PythonInteractiveWindow into AsyncToolWindowFactory 
            // because Package.ShowToolWindow doesn't call IVsAsyncToolWindowFactory.CreateToolWindow
            // which makes it impossible to fully override tool window creation
            if (toolWindowType == GuidList.guidPythonInteractiveWindowGuid) {
                var pyService = this.GetPythonToolsService();
                var category = SelectableReplEvaluator.GetSettingsCategory(id.ToString());
                string replId;
                try {
                    replId = pyService.LoadString("Id", category);
                } catch (Exception ex) when (!ex.IsCriticalException()) {
                    Debug.Fail("Could not load settings for interactive window.", ex.ToString());
                    replId = null;
                }

                if (string.IsNullOrEmpty(replId)) {
                    pyService.DeleteCategory(category);
                    return VSConstants.S_OK;
                }

                pyService.ComponentModel.GetService<InteractiveWindowProvider>().Create(replId, id);
                return VSConstants.S_OK;
            }

            return base.CreateToolWindow(ref toolWindowType, id);
        }

        internal static void NavigateTo(System.IServiceProvider serviceProvider, string filename, Guid docViewGuidType, int line, int col) {
            if (File.Exists(filename)) {
                VsUtilities.NavigateTo(serviceProvider, filename, docViewGuidType, line, col);
            }
        }

        internal static void NavigateTo(System.IServiceProvider serviceProvider, string filename, Guid docViewGuidType, int pos) {
            IVsTextView viewAdapter;
            IVsWindowFrame pWindowFrame;
            VsUtilities.OpenDocument(serviceProvider, filename, out viewAdapter, out pWindowFrame);

            ErrorHandler.ThrowOnFailure(pWindowFrame.Show());

            // Set the cursor at the beginning of the declaration.          
            int line, col;
            ErrorHandler.ThrowOnFailure(viewAdapter.GetLineAndColumn(pos, out line, out col));
            ErrorHandler.ThrowOnFailure(viewAdapter.SetCaretPos(line, col));
            // Make sure that the text is visible.
            viewAdapter.CenterLines(line, 1);
        }

        internal static ITextBuffer GetBufferForDocument(System.IServiceProvider serviceProvider, string filename) {
            IVsTextView viewAdapter;
            IVsWindowFrame frame;
            VsUtilities.OpenDocument(serviceProvider, filename, out viewAdapter, out frame);

            IVsTextLines lines;
            ErrorHandler.ThrowOnFailure(viewAdapter.GetBuffer(out lines));

            var adapter = serviceProvider.GetComponentModel().GetService<IVsEditorAdaptersFactoryService>();

            return adapter.GetDocumentBuffer(lines);
        }

        internal static IProjectLauncher GetLauncher(IServiceProvider serviceProvider, IPythonProject project) {
            var launchProvider = serviceProvider.GetUIThread().Invoke<string>(() => project.GetProperty(PythonConstants.LaunchProvider));

            IPythonLauncherProvider defaultLaunchProvider = null;
            foreach (var launcher in serviceProvider.GetComponentModel().GetExtensions<IPythonLauncherProvider>()) {
                if (launcher.Name == launchProvider) {
                    return serviceProvider.GetUIThread().Invoke<IProjectLauncher>(() => launcher.CreateLauncher(project));
                }

                if (launcher.Name == DefaultLauncherProvider.DefaultLauncherName) {
                    defaultLaunchProvider = launcher;
                }
            }

            // no launcher configured, use the default one.
            Debug.Assert(defaultLaunchProvider != null);
            return (defaultLaunchProvider != null) ?
                serviceProvider.GetUIThread().Invoke<IProjectLauncher>(() => defaultLaunchProvider.CreateLauncher(project)) :
                null;
        }

        internal static bool LaunchFile(IServiceProvider provider, string filename, bool debug, bool saveDirtyFiles) {
            var project = (IPythonProject)provider.GetProjectFromOpenFile(filename) ?? new DefaultPythonProject(provider, filename);
            try {
                var starter = GetLauncher(provider, project);
                if (starter == null) {
                    Debug.Fail("Failed to get project launcher");
                    return false;
                }

                if (saveDirtyFiles) {
                    var rdt = provider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
                    var rdt4 = provider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable4;
                    if (rdt != null && rdt4 != null) {
                        // The save operation may move the file, so adjust filename
                        // to the new location if necessary.
                        var launchFileCookie = rdt4.GetDocumentCookie(filename);

                        // Consider using (uint)(__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty | __VSRDTSAVEOPTIONS.RDTSAVEOPT_PromptSave)
                        // when VS settings include prompt for save on build
                        var saveOpt = (uint)__VSRDTSAVEOPTIONS.RDTSAVEOPT_SaveIfDirty;
                        var hr = rdt.SaveDocuments(saveOpt, null, VSConstants.VSITEMID_NIL, VSConstants.VSCOOKIE_NIL);
                        if (hr == VSConstants.E_ABORT) {
                            return false;
                        }

                        if (launchFileCookie != VSConstants.VSCOOKIE_NIL) {
                            var launchFileMoniker = rdt4.GetDocumentMoniker(launchFileCookie);
                            if (!string.IsNullOrEmpty(launchFileMoniker)) {
                                filename = launchFileMoniker;
                            }
                        }
                    }
                }

                starter.LaunchFile(filename, debug);
            } catch (MissingInterpreterException ex) {
                var interpreterRegistry = provider.GetComponentModel().GetService<IInterpreterRegistryService>();
                if (project.GetInterpreterFactory() == interpreterRegistry.NoInterpretersValue) {
                    OpenNoInterpretersHelpPage(provider, ex.HelpPage);
                } else {
                    var td = new TaskDialog(provider) {
                        Title = Strings.ProductTitle,
                        MainInstruction = Strings.FailedToLaunchDebugger,
                        Content = ex.Message
                    };
                    td.Buttons.Add(TaskDialogButton.Close);
                    td.ShowModal();
                }
                return false;
            } catch (NoInterpretersException ex) {
                OpenNoInterpretersHelpPage(provider, ex.HelpPage);
                return false;
            } catch (NoStartupFileException ex) {
                MessageBox.Show(ex.Message, Strings.ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            } catch (IOException ex) {
                MessageBox.Show(ex.Message, Strings.ProductTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        async Task<ToolWindowPane> IPythonToolsToolWindowService.GetWindowPaneAsync(Type windowType, bool create) {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            return await FindWindowPaneAsync(windowType, 0, create, DisposalToken) as ToolWindowPane;
        }

        async Task IPythonToolsToolWindowService.ShowWindowPaneAsync(Type windowType, bool focus) {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            var toolWindow = await ShowToolWindowAsync(windowType, 0, true, DisposalToken);
            if (focus && toolWindow.Content is System.Windows.UIElement content) {
                content.Focus();
            }
        }

        internal static void OpenNoInterpretersHelpPage(IServiceProvider serviceProvider, string page = null) {
            OpenVsWebBrowser(serviceProvider, page ?? PythonToolsInstallPath.GetFile("NoInterpreters.html"));
        }

        public static string InterpreterHelpUrl {
            get {
                return string.Format("https://go.microsoft.com/fwlink/?LinkId=299429&clcid=0x{0:X}",
                    CultureInfo.CurrentCulture.LCID);
            }
        }

        protected override object GetAutomationObject(string name) {
            if (name == "VsPython") {
                if (_autoObject == null) {
                    _autoObject = new PythonAutomation(this);
                }
                return _autoObject;
            }

            return base.GetAutomationObject(name);
        }

        public override bool IsRecognizedFile(string filename) {
            return ModulePath.IsPythonSourceFile(filename);
        }

        public override Type GetLibraryManagerType() => typeof(IPythonLibraryManager);

        internal override LibraryManager CreateLibraryManager() {
            return new PythonLibraryManager(this);
        }

        /////////////////////////////////////////////////////////////////////////////
        // Overriden Package Implementation

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
            Trace.WriteLine("Entering InitializeAsync() of: {0}".FormatUI(this));

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await base.InitializeAsync(cancellationToken, progress);

            AddService<IClipboardService>(new ClipboardService(), true);
            AddService<IPythonToolsToolWindowService>(this, true);
            AddService<PythonLanguageInfo>((container, serviceType) => new PythonLanguageInfo(this), promote: true);
            AddService<CustomDebuggerEventHandler>((container, serviceType) => new CustomDebuggerEventHandler(this), promote: true);
            AddService<IPythonToolsOptionsService>(PythonToolsOptionsService.CreateService, promote: true);
            AddService<IPythonToolsLogger>(PythonToolsLogger.CreateService, promote: true);
            AddService<PythonToolsService>(PythonToolsService.CreateService, promote: true);
            AddService<ErrorTaskProvider>(ErrorTaskProvider.CreateService, promote: true);
            AddService<CommentTaskProvider>(CommentTaskProvider.CreateService, promote: true);

            var solutionEventListener = new SolutionEventsListener(this);
            solutionEventListener.StartListeningForChanges();
            AddService<SolutionEventsListener>(solutionEventListener, true);
            
            // Enable the mixed-mode debugger UI context
            UIContext.FromUIContextGuid(DkmEngineId.NativeEng).IsActive = true;

            // Add our command handlers for menu (commands must exist in the .vsct file)
            RegisterCommands(GuidList.guidPythonToolsCmdSet,
                new OpenReplCommand(this, (int)PkgCmdIDList.cmdidReplWindow),
                new OpenReplCommand(this, PythonConstants.OpenInteractiveForEnvironment),
                new OpenDebugReplCommand(this),
                new ExecuteInReplCommand(this),
                new SendToReplCommand(this),
                new ViewEnvironmentStatusCommand(this),
                new FillParagraphCommand(this),
                new DiagnosticsCommand(this),
                new RemoveImportsCommand(this, true),
                new RemoveImportsCommand(this, false),
                new OpenInterpreterListCommand(this),
                new ImportWizardCommand(this),
                new ImportCoverageCommand(this),
                new ShowPythonViewCommand(this),
                new ShowCppViewCommand(this),
                new ShowNativePythonFrames(this),
                new UsePythonStepping(this),
                new ViewAllEnvironmentsCommand(this),
                new OpenWebUrlCommand(this, "https://go.microsoft.com/fwlink/?linkid=832525", PkgCmdIDList.cmdidWebPythonAtMicrosoft),
                new OpenWebUrlCommand(this, Strings.IssueTrackerUrl, PkgCmdIDList.cmdidWebPTVSSupport),
                new OpenWebUrlCommand(this, "https://go.microsoft.com/fwlink/?linkid=832517", PkgCmdIDList.cmdidWebDGProducts));

            RegisterCommands(
                CommandAsyncToOleMenuCommandShimFactory.CreateCommand(GuidList.guidPythonToolsCmdSet, (int)PkgCmdIDList.cmdidAddEnvironment, new AddEnvironmentCommand(this)),
                CommandAsyncToOleMenuCommandShimFactory.CreateCommand(GuidList.guidPythonToolsCmdSet, PythonConstants.FirstEnvironmentCmdId, new SwitchToEnvironmentCommand(this)),
                CommandAsyncToOleMenuCommandShimFactory.CreateCommand(GuidList.guidPythonToolsCmdSet, PythonConstants.InstallPythonPackage, new ManagePackagesCommand(this))
            );

            // Enable the Python debugger UI context
            UIContext.FromUIContextGuid(AD7Engine.DebugEngineGuid).IsActive = true;

            // The variable is inherited by child processes backing Test Explorer, and is used in PTVS
            // test discoverer and test executor to connect back to VS.
            Environment.SetEnvironmentVariable("_PTVS_PID", Process.GetCurrentProcess().Id.ToString());

            _statusBar = new VsStatusBar(this);

            AddEnvironmentsStatusBar();

            Trace.WriteLine("Leaving Initialize() of: {0}".FormatUI(this));
        }


        private void AddEnvironmentsStatusBar() {
            _environmentSwitcherStatusBar = new EnvironmentSwitcherStatusBar(this) { DataContext = new EnvironmentSwitcherStatusBarViewModel(this) };
            _disposables.TryAdd(_statusBar.AddItem(_environmentSwitcherStatusBar));
        }

        internal void ViewEnvironmentStatus() {
            _environmentSwitcherStatusBar?.ShowMenu();
        }

        public EnvDTE.DTE DTE {
            get {
                return (EnvDTE.DTE)GetService(typeof(EnvDTE.DTE));
            }
        }

        #region IVsComponentSelectorProvider Members

        public int GetComponentSelectorPage(ref Guid rguidPage, VSPROPSHEETPAGE[] ppage) {
            if (rguidPage == typeof(WebPiComponentPickerControl).GUID) {
                var page = new VSPROPSHEETPAGE();
                page.dwSize = (uint)Marshal.SizeOf(typeof(VSPROPSHEETPAGE));
                var pickerPage = new WebPiComponentPickerControl();
                if (_packageContainer == null) {
                    _packageContainer = new PackageContainer(this);
                }
                _packageContainer.Add(pickerPage);
                //IWin32Window window = pickerPage;
                page.hwndDlg = pickerPage.Handle;
                ppage[0] = page;
                return VSConstants.S_OK;
            }
            return VSConstants.E_FAIL;
        }

        /// <devdoc>
        ///     This class derives from container to provide a service provider
        ///     connection to the package.
        /// </devdoc>
        private sealed class PackageContainer : Container {
            private IUIService _uis;
            private AmbientProperties _ambientProperties;

            private System.IServiceProvider _provider;

            /// <devdoc>
            ///     Creates a new container using the given service provider.
            /// </devdoc>
            internal PackageContainer(System.IServiceProvider provider) {
                _provider = provider;
            }

            /// <devdoc>
            ///     Override to GetService so we can route requests
            ///     to the package's service provider.
            /// </devdoc>
            protected override object GetService(Type serviceType) {
                if (serviceType == null) {
                    throw new ArgumentNullException("serviceType");
                }
                if (_provider != null) {
                    if (serviceType.IsEquivalentTo(typeof(AmbientProperties))) {
                        if (_uis == null) {
                            _uis = (IUIService)_provider.GetService(typeof(IUIService));
                        }
                        if (_ambientProperties == null) {
                            _ambientProperties = new AmbientProperties();
                        }
                        if (_uis != null) {
                            // update the _ambientProperties in case the styles have changed
                            // since last time.
                            _ambientProperties.Font = (Font)_uis.Styles["DialogFont"];
                        }
                        return _ambientProperties;
                    }
                    object service = _provider.GetService(serviceType);

                    if (service != null) {
                        return service;
                    }
                }
                return base.GetService(serviceType);
            }
        }

        #endregion
    }
}
