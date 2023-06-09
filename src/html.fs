﻿module Tbd.Html

open Browser
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

module FsOption = FSharp.Core.Option

module Common = 
  [<Emit("$0[$1]")>]
  let getProperty<'T> (obj:obj) (name:string) : 'T = failwith "never"

  [<Emit("parseInt($0, $1)")>]
  let parseInt (s:string) (b:int) : int = failwith "JS"

  [<Emit("$0.toString($1)")>]
  let formatInt (i:int) (b:int) : string = failwith "JS"

  [<Emit("(typeof($0)=='number')")>]
  let isNumber(n:obj) : bool = failwith "!"

  [<Emit("($0 instanceof Date)")>]
  let isDate(n:obj) : bool = failwith "!"

  [<Emit("$0.toISOString()")>]
  let toISOString(o:obj) : string = failwith "!"

  [<Emit("new Date($0)")>]
  let asDate(n:float) : System.DateTime = failwith "!"

  [<Emit("($0 instanceof Date) ? $0.getTime() : $0")>]
  let dateOrNumberAsNumber(n:obj) : float = failwith "!"

  [<Emit("""($0.toLocaleString("en-US",{day:"numeric",year:"numeric",month:"short"}))""")>]
  let formatDate(d:obj) : string = failwith "!"

  [<Emit("""$0.toLocaleString("en-GB",{day:"numeric",year:"numeric",month:"long"})""")>]
  let formatLongDate(d:obj) : string = failwith "!"

  [<Emit("""($0.toLocaleString("en-US",{hour:"numeric",minute:"numeric",second:"numeric"}))""")>]
  let formatTime(d:obj) : string = failwith "!"

  [<Emit("""($0.toLocaleString("en-US",{hour:"numeric",minute:"numeric",second:"numeric"}) + ", " +
      $0.toLocaleString("en-US",{day:"numeric",year:"numeric",month:"short"}))""")>]
  let formatDateTime(d:obj) : string = failwith "!"

  [<Emit("(typeof($0)=='object')")>]
  let isObject(n:obj) : bool = failwith "!"

  [<Emit("Array.isArray($0)")>]
  let isArray(n:obj) : bool = failwith "!"

  [<Emit("isNaN($0)")>]
  let isNaN(n:float) : bool = failwith "!"

  let niceNumber num decs =
    let str = string num
    let dot = str.IndexOf('.')
    let before, after = 
      if dot = -1 then str, ""
      else str.Substring(0, dot), str.Substring(dot + 1, min decs (str.Length - dot - 1))
    let after = 
      if after.Length < decs then after + System.String [| for i in 1 .. (decs - after.Length) -> '0' |]
      else after 
    let mutable res = before
    if before.Length > 5 then
      for i in before.Length-1 .. -1 .. 0 do
        let j = before.Length - i
        if i <> 0 && j % 3 = 0 then res <- res.Insert(i, ",")
    if Seq.forall ((=) '0') after then res
    else res + "." + after

module Virtualdom = 
  [<Import("h","virtual-dom")>]
  let h(arg1: string, arg2: obj, arg3: obj[]): obj = failwith "JS only"

  [<Import("diff","virtual-dom")>]
  let diff (tree1:obj) (tree2:obj): obj = failwith "JS only"

  [<Import("patch","virtual-dom")>]
  let patch (node:obj) (patches:obj): Browser.Types.Node = failwith "JS only"

  [<Import("create","virtual-dom")>]
  let createElement (e:obj): Browser.Types.Node = failwith "JS only"

[<Fable.Core.Emit("jQuery($0).chosen()")>]
let private chosen (el:HTMLElement) : unit = failwith "JS"

[<Fable.Core.Emit("jQuery($0).on($1, $2)")>]
let private on (el:HTMLElement) (evt:string) (f:unit -> unit) : unit = failwith "JS"

[<Emit("$0[$1]")>]
let private getProperty (o:obj) (s:string) = failwith "!"

[<Emit("$0[$1] = $2")>]
let private setProperty (o:obj) (s:string) (v:obj) = failwith "!"

[<Fable.Core.Emit("event")>]
let private event () : Event = failwith "JS"

type DomAttribute = 
  | Event of (HTMLElement -> Event -> unit)
  | Attribute of string
  | Property of obj

type DomNode = 
  | Text of string
  | Delayed of string * DomNode * (string -> unit)
  | Element of ns:string * tag:string * attributes:(string * DomAttribute)[] * children : DomNode[] * onRender : (HTMLElement -> unit) option
  | Part of func:(HTMLElement -> unit)
  | Animate of string * int * DomNode

let createTree ns tag args children =
    let attrs = ResizeArray<_>()
    let props = ResizeArray<_>()
    for k, v in args do
      match k, v with 
      | k, Attribute v ->
          attrs.Add (k, box v)
      | k, Property o ->
          props.Add(k, o)
      | k, Event f ->
          props.Add ("on" + k, box (fun o -> f (getProperty o "target") (event()) ))
    let attrs = JsInterop.createObj attrs
    let ns = if ns = null || ns = "" then [] else ["namespace", box ns]
    let props = JsInterop.createObj (Seq.append (ns @ ["attributes", attrs]) props)
    let elem = Virtualdom.h(tag, props, children)
    elem

let mutable counter = 0

let rec renderVirtual (cache:System.Collections.Generic.IDictionary<_, _>) node = 
  match node with
  | Animate(id, hash, body) ->
      let switch, past = match cache.TryGetValue id with false, _ -> false, [] | _, (b, past) -> b, past
      let switch, past = 
        match past with 
        | [] -> not switch, [ hash, body ]
        | (prevHash, _)::_ when prevHash <> hash -> not switch, (hash, body)::past |> List.truncate 2
        | _ -> switch, past
      cache.[id] <- (switch, past)
      
      let prev = past |> Seq.tryFind (fun (h, _) -> h <> hash) 
      let aord1, aord2 = if switch then "anim-second", "anim-first" else "anim-first", "anim-second"
      let children = [| 
          match prev with 
          | Some(_, prev) -> yield Element(null, "div", [| "class", Attribute("anim anim-prev " + aord1) |], [| prev |], None)
          | _ -> ()
          yield Element(null, "div", [| "class", Attribute("anim anim-current " + aord2) |], [| body |], None)
        |]
      let children = if switch then Array.rev children else children

      let body = Element(null, "div", [| "class", Attribute "animated" |], children, None)
      renderVirtual cache body

  | Text(s) -> 
      box s

  | Element(ns, tag, attrs, children, None) ->
      createTree ns tag attrs (Array.map (renderVirtual cache) children)

  | Delayed(symbol, body, func) ->
      counter <- counter + 1
      let id = sprintf "delayed_%d" counter

      // Virtual dom calls our hook when it creates HTML element, but
      // we still need to wait until it is added to the HTML tree
      let rec waitForAdded n (el:HTMLElement) = 
        if el.parentElement <> null then 
          el?dataset?renderedSymbol <- symbol
          el?id <- id
          func id
        elif n > 0 then window.setTimeout((fun () -> waitForAdded  (n-1) el), 1) |> ignore
        else failwith "Delayed element was not created in time"

      // Magic as per https://github.com/Matt-Es`ch/virtual-dom/blob/master/docs/hooks.md
      let Hook = box(fun () -> ())
      Hook?prototype?hook <- fun (node:HTMLElement) propertyName previousValue ->
        if unbox node?dataset?renderedSymbol <> symbol then
          waitForAdded 10 node
      let h = createNew Hook ()

      createTree null "div" ["renderhk", Property h] [| renderVirtual cache body |]
  | Element _ ->
      failwith "renderVirtual: Does not support elements with after-render handlers"
  | Part _ ->
      failwith "renderVirtual: Does not support parts"

let rec render node = 
  match node with
  | Animate _ ->
      failwith "Animate not supported"

  | Text(s) -> 
      document.createTextNode(s) :> Node, ignore

  | Delayed(_, _, func) ->
      counter <- counter + 1
      let el = document.createElement("div")
      el.id <- sprintf "delayed_%d" counter
      el :> Node, (fun () -> func el.id)

  | Part(func) ->
      let el = document.createElement("div")
      el :> Node, (fun () -> func el)

  | Element(ns, tag, attrs, children, f) ->
      let el = 
        if ns = null || ns = "" then document.createElement(tag)
        else document.createElementNS(ns, tag) :?> HTMLElement
      let rc = Array.map render children
      for c, _ in rc do el.appendChild(c) |> ignore
      for k, a in attrs do 
        match a with
        | Property(o) -> setProperty el k o
        | Attribute(v) -> el.setAttribute(k, v)
        | Event(f) -> el.addEventListener(k, f el)
      let onRender () = 
        for _, f in rc do f()
        f |> FsOption.iter (fun f -> f el)
      el :> Node, onRender

let renderTo (node:HTMLElement) dom = 
  while box node.lastChild <> null do ignore(node.removeChild(node.lastChild))
  let el, f = render dom
  node.appendChild(el) |> ignore
  f()

let createVirtualDomAsyncApp id initial r u = 
  let event = new Event<'E>()
  let stateChanged = new Event<'S>()
  let eventTriggered = new Event<_>()
  let trigger e = event.Trigger(e)  
  let mutable container = document.createElement("div") :> Node
  document.getElementById(id).innerHTML <- ""
  document.getElementById(id).appendChild(container) |> ignore
  let mutable tree = Fable.Core.JsInterop.createObj []
  let mutable state = initial
  let events = ResizeArray<_>()
  let cache = System.Collections.Generic.Dictionary<_, _>()
  
  let setState newState = 
    state <- newState
    stateChanged.Trigger state
    let newTree = r (events :> seq<_>) trigger state |> renderVirtual cache
    let patches = Virtualdom.diff tree newTree
    container <- Virtualdom.patch container patches
    tree <- newTree 
  
  setState initial
  let mutable queue = []
  let mutable running = false
  event.Publish.Add(fun e -> Async.StartImmediate <| async { 
    try
      eventTriggered.Trigger(e)
      //printfn "Adding to queue: %A" e
      queue <- queue @ [e]
      if running then () //printfn "Already processing."
      else 
        running <- true
        //printfn "Processing events!"
        while queue.Length > 0 do
          let e = queue.Head
          queue <- queue.Tail
          //printfn "Process: %A" e
          let! s = u trigger state e
          events.Add(e)
          setState s 
          //printfn "Done with: %A" e
        //printfn "Finished processing."
        running <- false
    with e ->
      Browser.Dom.console.error(e)
    })
  trigger, setState, stateChanged.Publish, eventTriggered.Publish


let createVirtualDomApp id initial r u = 
  let event = new Event<'T>()
  let trigger e = event.Trigger(e)  
  let mutable container = document.createElement("div") :> Node
  document.getElementById(id).innerHTML <- ""
  document.getElementById(id).appendChild(container) |> ignore
  let mutable tree = Fable.Core.JsInterop.createObj []
  let mutable state = initial
  let cache = System.Collections.Generic.Dictionary<_, _>()

  let setState newState = 
    //printfn "SET STATE: %A" newState
    state <- newState
    let newTree = r trigger state |> renderVirtual cache
    let patches = Virtualdom.diff tree newTree
    container <- Virtualdom.patch container patches
    tree <- newTree
  
  setState initial
  event.Publish.Add(fun e -> setState (u state e))
  trigger, setState
  
let text s = Text(s)
let (=>) k v = k, Attribute(v)
let (=!>) k f = k, Event(f)


type El(ns) = 
  member x.Namespace = ns
  static member (?) (el:El, n:string) = fun a b ->
    let n, f = 
      if n <> "chosen" then n, None
      else "select", Some (fun el ->
        chosen el
        for k, v in a do
          match v with
          | Event f -> on el k (fun () -> f el (event()))
          | _ -> ()
      )
    Element(el.Namespace, n, Array.ofList a, Array.ofList b, f)

  member x.delayed sym body f =
    Delayed(sym, body, f)

  member x.anim id hash body = 
    Animate(id, hash, body)

  member x.part (initial:'State) (fold:'State -> 'Event -> 'State) = 
    let evt = Control.Event<_>()
    let mutable state = initial
    let mutable container = None
    let mutable renderer = None
    let render () =
      match container, renderer with
      | Some el, Some r -> r state |> renderTo el
      | _ -> ()
    evt.Publish.Add(fun e -> state <- fold state e; render ())

    evt.Trigger,
    fun (r:'State -> DomNode) ->
      renderer <- Some r
      Part(fun el -> 
        container <- Some el
        render() )

let h = El(null)
let s = El("http://www.w3.org/2000/svg")
