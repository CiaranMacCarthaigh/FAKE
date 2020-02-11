[<RequireQualifiedAccess>]
module Fake.Tools.Octo

open Fake.Core
open Fake.IO
open Fake.IO.Globbing
open Fake.IO.FileSystemOperators
open System
open System.IO
open System.Runtime.CompilerServices

[<assembly: InternalsVisibleTo("Fake.Core.UnitTests")>]
do()

/// Octo.exe server options
type ServerOptions = {
    /// The base URL for your Octopus server
    ServerUrl: string

    /// Your API key; retrieved from the user profile page.
    ApiKey: string }

/// Common Octo.exe CLI params
type Options = {
    ToolName            : string
    ToolPath            : string
    WorkingDirectory    : string
    Server              : ServerOptions
    Timeout             : TimeSpan }

/// Options for creating a new release
type CreateReleaseOptions = {
    /// Name of the project
    Project                 : string

    /// Release number to use for the new release
    Version                 : string

    /// Default version of all packages to use for this release
    PackageVersion          : string

    /// Version number to use for a package in the release
    Packages                : string list

    /// A folder containing NuGet packages from which we should get versions
    PackagesFolder          : string option

    /// Release Notes for the new release
    ReleaseNotes            : string

    /// Path to a file that contains Release Notes for the new release
    ReleaseNotesFile        : string

    /// If a release with the version number already exists, ignore it
    IgnoreExisting          : bool

    /// Channel to use for the new release
    Channel                 : string option

    /// Ignore package version matching rules
    IgnoreChannelRules      : bool

    ///common parameters
    Common: Options}

/// Options for deploying a release to an environment
type DeployReleaseOptions = {
    /// Name of the project
    Project                     : string

    /// Environment to deploy to
    DeployTo                    : string

    /// Version number of the release to deploy; Specify "latest" for
    /// the latest release
    Version                     : string

    /// If a project is configured to skip packages with already-installed
    /// versions, override this setting to force re-deployment
    Force                       : bool

    /// Whether to wait synchronously for deployment to finish
    WaitForDeployment           : bool

    /// Don't print the raw log of failed tasks
    NoRawLog                    : bool

    /// Show progress of the deployment.
    /// (Sets --waitfordeployment and --norawlog to true.)
    Progress                    : bool

    /// Specifies maximum time that deployment can take
    /// (default: 10 minutes)
    DeploymentTimeout           : TimeSpan option

    /// Specifies how much time should elapse between deployment status
    /// checks (default: 10 seconds)
    DeploymentCheckSleepCycle   : TimeSpan option

    /// A comma-separated list of machine names to target in the
    /// deployed environment. If not specified, all machines in
    /// the environment will be considered.
    SpecificMachines            : string option

    /// Channel to use for the new release
    Channel                     : string option

    /// Common parameters
    Common                      : Options}

/// Options for deleting a range of releases in a project
type DeleteReleasesOptions = {
    /// Name of the project
    Project     : string

    /// Minimum (inclusive) version number for the range of versions to delete
    MinVersion  : string

    /// Maximum (inclusive) version number for the range of versions to delete
    MaxVersion  : string

    /// If specified, only releases
    /// associated with the channel will be deleted;
    /// specify this argument multiple times to target
    /// multiple channels
    Channel     : string option

    /// Common parameters
    Common      : Options}

type PushOptions = {
    // paths to one or more packages to push to the server
    Packages : string list
    /// if the package already exists, should this package overwrite it?
    ReplaceExisting : bool
    /// Common parameters
    Common: Options }

/// Option type for selecting one command
type internal Command =
| CreateRelease of CreateReleaseOptions * DeployReleaseOptions option
| DeployRelease of DeployReleaseOptions
| DeleteReleases of DeleteReleasesOptions
| ListEnvironments
| Push of PushOptions

/// Default server options.
let private serverOptions = { ServerUrl = ""; ApiKey = ""; }

/// Default parameters to call octo.exe.
let internal commonOptions =
    let toolName = "Octo.exe"
    { ToolPath = Tools.findToolFolderInSubPath toolName (Directory.GetCurrentDirectory() @@ "tools" @@ "OctopusTools")
      ToolName = toolName
      Server = serverOptions
      Timeout = TimeSpan.MaxValue
      WorkingDirectory = "" }

/// Default options for 'CreateRelease'
let internal releaseOptions = {
    Project = ""; Version = ""; PackageVersion = ""; Packages = [];
    PackagesFolder = None; ReleaseNotes = ""; ReleaseNotesFile = "";
    IgnoreExisting = false; Channel = None; IgnoreChannelRules = false; Common = commonOptions}

/// Default options for 'DeployRelease'
let internal deployOptions = {
    Project = ""; DeployTo = ""; Version = ""; Force = false; WaitForDeployment = false;
    DeploymentTimeout = None; DeploymentCheckSleepCycle = None; SpecificMachines = None;
    NoRawLog = false; Progress = false; Channel = None; Common = commonOptions }

/// Default options for 'DeleteReleases'
let internal deleteOptions = {
    Project = ""; MinVersion = ""; MaxVersion = ""; Channel = None; Common = commonOptions }

/// Default options for 'Push'
let internal pushOptions = {
    Packages = []; ReplaceExisting = false; Common = commonOptions}

let private optionalStringParam p o =
    match o with
    | Some s -> sprintf "--%s=%s" p s
    | None -> ""

let private optionalObjParam p o =
    match o with
    | Some x -> sprintf "--%s=%s" p (x.ToString())
    | None -> ""

let private stringListParam p os =
    let sb = Text.StringBuilder()
    for o in os do
        sb.Append (sprintf " --%s=%s" p (o.ToString())) |> ignore
    sb.ToString()

let private flag p b = if b then sprintf " --%s" p else ""

let private appendArray paramName values arguments =
    (arguments, values)
    ||> Seq.fold (fun args value -> Arguments.appendNotEmpty paramName value args)

let private releaseCommandLine (createReleaseOptions: CreateReleaseOptions) =
    Arguments.Empty
    |> Arguments.appendRaw "create-release"
    |> Arguments.appendNotEmpty "--project" createReleaseOptions.Project
    |> Arguments.appendNotEmpty "--version" createReleaseOptions.Version
    |> Arguments.appendOption "--version" createReleaseOptions.Channel
    |> Arguments.appendNotEmpty "--packageVersion" createReleaseOptions.PackageVersion
    |> appendArray "--package" createReleaseOptions.Packages
    |> Arguments.appendOption "--packagesFolder" createReleaseOptions.PackagesFolder
    |> Arguments.appendNotEmpty "--releasenotes" createReleaseOptions.ReleaseNotes
    |> Arguments.appendNotEmpty "--releasenotesfile" createReleaseOptions.ReleaseNotesFile
    |> Arguments.appendIf createReleaseOptions.IgnoreExisting "--ignoreexisting"
    |> Arguments.appendIf createReleaseOptions.IgnoreChannelRules "--ignorechannelrules"

let private appendDeploymentOptions (opts:DeployReleaseOptions) existingArguments =
    existingArguments
    |> Arguments.appendNotEmpty "--project" opts.Project            // Is this used by Octopus? It doesn't appear in the deploy-release help, and the create-release version has its own arguments.
    |> Arguments.appendNotEmpty "--deployto" opts.DeployTo
    |> Arguments.appendNotEmpty "--version" opts.Version            // Same.
    |> Arguments.appendIf opts.Force "--force"
    |> Arguments.appendIf opts.WaitForDeployment "--waitfordeployment"
    |> Arguments.appendIf opts.NoRawLog "--norawlog"
    |> Arguments.appendIf opts.Progress "--progress"
    |> Arguments.appendOption "--deploymenttimeout" (opts.DeploymentTimeout |> Option.map string)
    |> Arguments.appendOption "--deploymentchecksleepcycle" (opts.DeploymentCheckSleepCycle |> Option.map string)
    |> Arguments.appendOption "--specificmachines" opts.SpecificMachines
    |> Arguments.appendOption "--channel" opts.Channel

let private deleteCommandLine (opts:DeleteReleasesOptions) =
    [ (optionalStringParam "project" (String.liftString opts.Project))
      (optionalStringParam "minversion" (String.liftString opts.MinVersion))
      (optionalStringParam "maxversion" (String.liftString opts.MaxVersion))
      (optionalStringParam "channel" (opts.Channel)) ]
    |> List.filter String.isNotNullOrEmpty

let private serverCommandLine (opts:ServerOptions) =
    [ (optionalStringParam "server" (String.liftString opts.ServerUrl))
      (optionalStringParam "apikey" (String.liftString opts.ApiKey)) ]
    |> List.filter String.isNotNullOrEmpty

let private appendServerOptions (opts: ServerOptions) arguments =
    arguments
    |> Arguments.appendNotEmpty "--server" opts.ServerUrl
    |> Arguments.appendNotEmpty "--apikey" opts.ApiKey

let private pushCommandLine (opts : PushOptions) =
    Arguments.Empty
    |> Arguments.appendRaw "push"
    |> fun pushArgs ->
        opts.Packages
        |> List.fold (fun args package -> Arguments.appendNotEmpty "--package" package args) pushArgs
    |> Arguments.appendIf opts.ReplaceExisting "--replace-existing"

/// Maps a command to string input for the octopus tools cli.
let internal commandLine command =
    match command with
    | CreateRelease (opts, None) ->
        releaseCommandLine opts
    | CreateRelease (opts, Some dopts) ->
        releaseCommandLine opts
        |> appendDeploymentOptions dopts
    | DeployRelease opts ->
        Arguments.Empty
        |> Arguments.appendRaw "deploy-release"
        |> appendDeploymentOptions opts
    | DeleteReleases commandOptions ->
        deleteReleasesCommandLine commandOptions
        //"delete-releases" :: (deleteCommandLine opts)
//    | ListEnvironments ->
//        ["list-environments"]
    | Push commandOptions ->
        pushCommandLine commandOptions

let private exec command options =
    let serverOptionsForTracing opts =
        { opts with ApiKey = "(Removed for security purposes)" }

    let tool = Path.Combine(options.ToolPath, options.ToolName)
    let commandArgs = commandLine command
    let applicationArgs =
        commandArgs
        |> appendServerOptions options.Server
    let traceArgs =
        commandArgs
        |> appendServerOptions (serverOptionsForTracing options.Server)

    let commandString = command.ToString()

    use __ = Trace.traceTask "Octo " commandString
    traceArgs
    |> Arguments.toStartInfo
    |> sprintf "%s %s" tool
    |> Trace.trace

    let result =
        RawCommand (tool, applicationArgs)
        |> CreateProcess.fromCommand
        |> CreateProcess.withWorkingDirectory options.WorkingDirectory
        |> CreateProcess.withTimeout options.Timeout
        |> Proc.run
        |> (fun finishedProccess -> finishedProccess.ExitCode)

    match result with
    | 0 ->
        __.MarkSuccess()
        result
    | _ ->
        __.MarkFailed()
        result

/// Creates a release and returns the exit code.
let createReleaseWithExitCode setParams =
    let options = setParams releaseOptions
    exec (CreateRelease (options, None)) options.Common

/// Creates a release, and optionally deploys it to one or more environments and returns the exit code.
let createReleaseAndDeployWithExitCode setReleaseParams setDeployParams =
    let releaseOptions = setReleaseParams releaseOptions
    let deployOptions = setDeployParams deployOptions
    exec (CreateRelease (releaseOptions, deployOptions)) releaseOptions.Common

/// Deploys releases that have already been created and returns the exit code.
let deployReleaseWithExitCode setParams =
    let options = setParams deployOptions
    exec (DeployRelease options) options.Common

/// Deletes a range of releases and returns the exit code.
let deleteReleasesWithExitCode setParams =
    let options = setParams deleteOptions
    exec (DeleteReleases options) options.Common

/// Lists all environments and returns the exit code.
let listEnvironmentsWithExitCode setParams =
    let options = setParams commonOptions
    exec ListEnvironments options

/// Pushes one or more packages to the Octopus built-in repository and returns the exit code.
let pushWithExitCode setParams =
    let options = setParams pushOptions
    exec (Push options) options.Common


let private handleIgnoreExitCode commandString result =
    match result with
    | 0 ->
        ()
    | _ ->
        failwithf "Octo %s failed. Process finished with exit code %i" commandString result

/// Creates a release.
let createRelease setParams =
    let commandLine = (CreateRelease ((setParams releaseOptions), None)).ToString()
    createReleaseWithExitCode setParams
    |> (handleIgnoreExitCode <| commandLine)

/// Creates a release, and optionally deploys it to one or more environments.
let createReleaseAndDeploy setReleaseParams setDeployParams =
    let commandLine = (CreateRelease ((setReleaseParams releaseOptions), (setDeployParams deployOptions))).ToString()
    createReleaseAndDeployWithExitCode setReleaseParams setDeployParams
    |> (handleIgnoreExitCode <| commandLine )

/// Deploys releases that have already been created.
let deployRelease setParams =
    let commandLine = (DeployRelease (setParams deployOptions)).ToString()
    deployReleaseWithExitCode setParams
    |> (handleIgnoreExitCode <| commandLine )

/// Deletes a range of releases.
let deleteReleases setParams =
    let commandLine = (DeleteReleases ((setParams deleteOptions))).ToString()
    deleteReleasesWithExitCode setParams
    |> (handleIgnoreExitCode <| commandLine )

/// Lists all environments.
let listEnvironments setParams =
    let commandLine = (ListEnvironments).ToString()
    listEnvironmentsWithExitCode setParams
    |> (handleIgnoreExitCode <| commandLine)

/// Pushes one or more packages to the Octopus built-in repository.
let push setParams =
    let commandLine = (Push ((setParams pushOptions))).ToString()
    pushWithExitCode setParams
    |> (handleIgnoreExitCode <| commandLine)
