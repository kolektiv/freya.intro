(**
- title : Freya
- description : A functional-first web stack in F#
- author : Andrew Cherry (@kolektiv) and Ryan Riley (@panesofglass)
- theme : night
- transition : default

***

*)

(*** hide ***)
#I "../packages"
#r "Aether/lib/net40/Aether.dll"
#r "System.Json/lib/net40/System.Json.dll"
#r "ReadOnlyCollectionInterfaces/lib/NET40-client/ReadOnlyCollectionInterfaces.dll"
#r "ReadOnlyCollectionExtensions/lib/NET40-client/ReadOnlyCollectionExtensions.dll"
#r "LinqBridge/lib/net20/LinqBridge.dll"
#r "FsControl/lib/net40/FsControl.Core.dll"
#r "FSharpPlus/lib/net40/FSharpPlus.dll"
#r "Fleece/lib/NET40/Fleece.dll"
#r "FParsec/lib/net40-client/FParsecCS.dll"
#r "FParsec/lib/net40-client/FParsec.dll"
#r "Owin/lib/net40/owin.dll"
#r "Freya.Core/lib/net40/Freya.Core.dll"
#r "Freya.Pipeline/lib/net40/Freya.Pipeline.dll"
#r "Freya.Recorder/lib/net40/Freya.Recorder.dll"
#r "Freya.Types/lib/net40/Freya.Types.dll"
#r "Freya.Types.Uri/lib/net40/Freya.Types.Uri.dll"
#r "Freya.Types.Language/lib/net40/Freya.Types.Language.dll"
#r "Freya.Types.Http/lib/net40/Freya.Types.Http.dll"
#r "Freya.Types.Cors/lib/net40/Freya.Types.Cors.dll"
#r "Freya.Machine/lib/net40/Freya.Machine.dll"
#r "Freya.Router/lib/net40/Freya.Router.dll"
#r "Freya.Machine.Router/lib/net40/Freya.Machine.Router.dll"
#r "Unquote/lib/net40/Unquote.dll"

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Owin
open Freya.Core
open Freya.Core.Operators
open Freya.Pipeline
open Freya.Pipeline.Operators
open Freya.Types.Http
open Freya.Machine
open Freya.Router
open Freya.Machine.Router
open Freya.Inspector
open Freya.Machine.Inspector
open Freya.Router.Inspector
open Swensen.Unquote

(**

# Freya

## A functional web stack in F#

![Freya logo]()

***

## (Optimistic) Contents

* Introduction (5 mins)
* [OWIN](http://owin.org/) (5 mins)
* Freya Stack (10 mins)
  * Tour
  * Lenses
* Todo Backend - Review (10 mins)
* Static File Server - Walkthrough (10 mins)
* Next / Questions (? mins)

***

# Introduction

General Rambling

***

## Who?

* Andrew Cherry ([@kolektiv](https://twitter.com/kolektiv))
* Ryan Riley ([@panesofglass](https://twitter.com/panesofglass))
* ... others? Collaboration is welcome!

***

## Ecosystem (F# Web)

* [WebSharper](http://websharper.com/)
* [Suave](http://suave.io/)
* [Frank](http://frankfs.net/)
* [Taliesin](https://github.com/frank-fs/taliesin)
* [Dyfrig](https://github.com/panesofglass/dyfrig)
* [other projects usable from F#, but implemented in "something else"]
* [other projects I've forgotten / not found]

***

## History

* **2010 - Frank** - combinator library using `System.Net.Http` types
* **2013 - Dyfrig** - initial F# OWIN helpers, eventually became a small library
* **2013 - Taliesin** - OWIN routing middleware
* **2014 - Frost** - experiments in "machine" style web processing in F#
* ...
* **2015 - Freya**

***

## Naming

* Dyfrig
* Taliesin
* ...
* Common theme -- names that nobody can pronounce
* Alternative spelling "Freyja" rejected due to increasing self-awareness

***

# OWIN

Integration with existing standards, when possible

***

## [OWIN](http://owin.org/)

*)

Func<IDictionary<string, obj>, Task>

(**

* Standard contract between servers and apps/frameworks
* Several server implementations, including IIS
* Reasonably well followed standard

***

## OWIN Design

* OWIN design is very simple for [historically meaningful reasons](http://panesofglass.github.io/history-of-owin/)
* Assumes mutation and side effects
* Uses simple types and works with any .NET language

***

## OWIN Design

* `Task`s (not `Task<T>`)
* Dictionary of state (`IDictionary<string, obj>`)
* Defined keys contain boxed objects of known types (some keys are optional)
  * e.g., request headers are "owin.RequestHeaders" defined as an `IDictionary<string, string[]>`
  * spec includes rules governing side effects in some cases

***

## OWIN Design

* Servers should take action when certain elements in the environment dictionary change
* E.g., writing to the body stream, "owin.ResponseBody" typed as `System.IO.Stream`, flushes the headers, and you should no longer be able to write headers
* This design makes life difficult from the perspective of functional purity

***

# Freya Stack

A Tour

***

## Freya Architectural Principles

* Stack rather than a monolithic framework
* Building blocks for higher level abstractions
* Compatibility with external libraries, e.g. existing OWIN middleware

***

## Freya "Ethical" Principles

* Work with and not against existing abstractions
* Make it easy/trivial to do the right thing
* Use the strengths of F# to make it hard/impossible to do the wrong thing

***

![Freya stack](images/freya-stack.png)

***

## `Freya.Core`

* Basic abstractions over OWIN
* `freya` computation expression (the wrapping abstraction for our mutable state -- we will never speak of this in polite conversation)
* Set of operators to use `freya` computation expressions in a more concise way
* Some basic functions and **lenses** to give access to the state in a controllable way

***
*)

type Freya<'T> =
    FreyaState -> Async<'T * FreyaState>
(**

Roughly equivalent to Erlang's webmachine signature:

```
f(ReqData, State) ->
    {RetV, ReqData, State}.
```

***
*)

type FreyaState =
    { Environment: FreyaEnvironment
      Meta: FreyaMetaState }

    static member internal EnvironmentLens =
        (fun x -> x.Environment), 
        (fun e x -> { x with Environment = e })

    static member internal MetaLens =
        (fun x -> x.Meta), 
        (fun m x -> { x with Meta = m })

and FreyaMetaState =
    { Memos: Map<Guid, obj> }

    static member internal MemosLens =
        (fun x -> x.Memos),
        (fun m x -> { x with Memos = m })

(**
***

## Lenses?

*)

let ``getLM, setLM, modLM behave correctly`` () =
    let m =
        freya {
            do! setLM answerLens 42
            let! v1 = getLM answerLens

            do! modLM answerLens ((*) 2)
            let! v2 = getLM answerLens

            return v1, v2 }

    let result = run m

    fst result =? (42, 84)

(**
***

## OWIN Integration

*)

/// Type alias of <see cref="FreyaEnvironment" /> in terms of OWIN.
type OwinEnvironment =
    FreyaEnvironment

/// Type alias for the F# equivalent of the OWIN AppFunc signature.
type OwinApp = 
    OwinEnvironment -> Async<unit>

/// Type alias for the OWIN AppFunc signature.
type OwinAppFunc = 
    Func<OwinEnvironment, Task>

(**
***
## Use OWIN in Freya
*)

let ``freya computation can compose with an OwinAppFunc`` () =
    let app =
        OwinAppFunc(fun (env: OwinEnvironment) ->
            env.["Answer"] <- 42
            Task.FromResult<obj>(null) :> Task)

    let converted = OwinAppFunc.toFreya app

    let m =
        freya {
            do! converted
            let! v1 = getLM answerLens
            return v1 }
    
    let result = run m
    fst result =? 42

(**
***
## Convert Freya to OWIN
*)

let ``freya computation can roundtrip to and from OwinAppFunc`` () =
    let app = setLM answerLens 42

    let converted =
        app
        |> OwinAppFunc.fromFreya
        |> OwinAppFunc.toFreya

    let m =
        freya {
            do! converted
            let! v1 = getLM answerLens
            return v1 }
    
    let result = run m
    fst result =? 42

(**
***

## `Freya.Pipeline`

* Very small and simple -- all about composing `freya` computations in a way that represents a continue/halt processing pipeline
* A pipeline is simply a `freya` computation that returns `Next` or `Halt` (`FreyaPipelineChoice` cases)
* Single, simple operator: `>?=`

***
*)

let ``pipeline executes both monads if first returns next`` () =
    let o1 = modM (fun x -> x.Environment.["o1"] <- true; x) *> next
    let o2 = modM (fun x -> x.Environment.["o2"] <- true; x) *> next

    let choice, env = run (o1 >?= o2)

    choice =? Next
    unbox env.Environment.["o1"] =? true
    unbox env.Environment.["o2"] =? true

let ``pipeline executes only the first monad if first halts`` () =
    let o1 = modM (fun x -> x.Environment.["o1"] <- true; x) *> halt
    let o2 = modM (fun x -> x.Environment.["o2"] <- true; x) *> next

    let choice, env = run (o1 >?= o2)

    choice =? Halt
    unbox env.Environment.["o1"] =? true
    unbox env.Environment.["o2"] =? false

(**
***

## `Freya.Recorder`

* Build introspection into the framework at a low level
* Provide some infrastructure for recording metadata about processing that more specific implemenations can use
* For example, `Freya.Machine` records the execution process so it can be examined later

***

## `Freya.Types.*`

* Set of libraries providing F# types which map (very closely) to various specifications, such as HTTP, URI, LanguageTag, etc.
* These are used throughout the higher level stack projects
* Always favor strongly-typed representations of data over strings
* Provides parsers, formatters (statically on the types) and lenses from state to that type (either total or partial)

***

## Really?

* Why not use `System.Net.Whatever`?
* Well ...

***

![Ask the UriKind. One. More. Time.](images/ask-the-urikind.png)

***

## `Freya.Types.*`

* Types and parsers for when you don't already know everything about the string you've been given
* Types which map closely to HTTP specifications
* Types which can distinguish between different kinds of URIs being valid in different places
* Types which can actually express languages that aren't "en-US"
* ("hy-Latn-IT-arevela"? Of course we support Aremenian with a Latin script as spoken in Northern Italy why do you ask?)

***

## `Freya.Router`

* A simple, trie-based router, does pretty much what you'd expect
* Doesn't try and do anything but route requests to pipelines
* (and is itself a pipeline -- everything's composable / nestable!)

***

## `Freya.Machine`

* A "machine" style resource definition / processing library
* Inspired by projects like webmachine (Erlang) and Liberator (Clojure)
* Adds types

***

## Machine Style Frameworks?

* Modeled as a graph, or state machine, of how to respond to a request
* Configured by choosing to override certain aspects (decisions, handlers, etc.)
* Each resource is therefore the default graph, plus a set of overrides

***

![Freya visual debugging](images/graph.png)

***

## `Freya.Inspector`

* Built-in introspection
* Has an extensibility model (WIP)
* Right now provides an API; UI in-progress

***

## `Freya.*.Inspector`

* Component-specific extensions to the inspector, currently providing component-specific JSON for the inspection API
* Will provide UI extensions, too, but haven't decided on the best approach to that (suggestions welcome, of course)

***

# Todo Backend

***

## Todo Backend

* A standard, simple "thing" to implement to help compare approaches
* Inspired by TodoMVC, for comparing front-end frameworks
* Here: http://todobackend.com/

***

## Demo: Todo Backend

***

# Static File Server

***

## Static File Server

* How do you approach building something using `Freya.*`?
* Let's build a tiny little static file server and see how to extend it

***

## Demo: Static File Server

***

## Next for Freya

* Full release! Very soon ...
* Inspectors / UI - in progress
* Documentation - also very soon!
* http://github.com/freya-fs/freya

***

# Questions?

*)
