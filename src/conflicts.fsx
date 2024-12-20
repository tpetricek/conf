#nowarn "3353"
#load "utils/utils.fs" "utils/parsec.fs" "utils/ordlist.fs" "doc/doc.fs" "doc/apply.fs" "doc/merge.fs" "represent.fs" "eval.fs" "demos.fs" 
open Denicek
open Denicek.Doc
open Denicek.Demos
open Denicek.Merge
open Denicek.Doc.Format

// --------------------------------------------------------------------------------------
// Type checking - we can infer type of the resulting document!
// --------------------------------------------------------------------------------------
(*
type PrimitiveType =
  | TyNumber 
  | TyString 

type Type = 
  | TyRecord of string * list<string * Type>
  | TyList of string * Type
  | TyPrimitive of PrimitiveType
  | TyReference
  | TyAny

let commonType t1 t2 =
  match t1, t2 with
  | _ when t1 = t2 -> t1
  | t, TyAny | TyAny, t -> t
  | _ -> failwith $"commonType: No common type for {t1} and {t2}"

let rec typeOfNode nd = 
  match nd with 
  | Primitive(String _) -> TyPrimitive(TyString)
  | Primitive(Number _) -> TyPrimitive(TyNumber)
  | Reference(_) -> TyReference
  | Record(tag, chld) -> TyRecord(tag, [for fld, nd in chld -> fld, typeOfNode nd])
  | List(tag, chld) -> TyList(tag, List.map typeOfNode chld |> List.fold commonType TyAny)

let rec getTypePart sel typ = 
  match sel, typ with 
  | [], typ -> typ
  | Field fsel::sel, TyRecord(_, chld) -> 
      chld |> List.pick (fun (f, ty) -> if f = fsel then Some(getTypePart sel ty) else None)
  | (All | Index _ | Tag _)::sel, TyList(_, ty) -> getTypePart sel ty 
  | _ -> failwith $"getTypePart: Incompatible type ({typ}) and selector {formatSelector sel}"

let rec transformTypePart sel typ op = 
  match sel, typ with 
  | [], typ -> op typ
  | Field fsel::sel, TyRecord(t, chld) -> 
      let nchld = chld |> List.map (fun (f, ty) -> 
        if f = fsel then f, transformTypePart sel ty op else f, ty )
      TyRecord(t, nchld)
  | (All | Index _ | Tag _)::sel, TyList(t, ty) -> TyList(t, transformTypePart sel ty op)
  | _ -> failwith $"transformTypePart: Incompatible type ({typ}) and selector {formatSelector sel}"

let transformRecordPart sel typ op = 
  transformTypePart sel typ (function
    | TyRecord(t, flds) -> op t flds
    | ty -> failwith $"transformType.RecordAdd - expected record but got {ty}")

let transformListPart sel typ op = 
  transformTypePart sel typ (function
    | TyList(t, ty) -> op t ty
    | ty -> failwith $"transformType.ListAppend - expected list but got {ty}")

let transformType typ ed = 
  match ed.Kind with 
  | Value(RecordAdd(tgt, fld, nd)) -> 
      transformRecordPart tgt typ (fun t flds -> TyRecord(t, flds @ [fld, typeOfNode nd]))

  | Shared(StructuralKind, ListAppend(tgt, nd)) -> 
      transformListPart tgt typ (fun t ty -> TyList(t, commonType ty (typeOfNode nd)))

  | Shared(StructuralKind, ListAppendFrom(tgt, src)) -> 
      transformListPart tgt typ (fun t ty -> TyList(t, commonType ty (getTypePart src typ)))

  | Shared(StructuralKind, RecordRenameField(tgt, fold, fnew)) -> 
      transformRecordPart tgt typ (fun t flds -> 
        TyRecord(t, List.map (fun (f, ty) -> (if f = fold then fnew else f), ty) flds))

  | Shared(StructuralKind, WrapRecord(fld, tag, tgt)) -> 
      transformTypePart tgt typ (fun ty -> TyRecord(tag, [fld, ty]))

  | Shared(StructuralKind, UpdateTag(tgt, told, tnew)) -> 
      transformTypePart tgt typ (function
        | TyRecord(t, flds) when t = told -> TyRecord(tnew, flds)
        | TyList(t, ty) when t = told -> TyList(tnew, ty)
        | ty -> ty)

  | Shared(StructuralKind, Copy(tgt, src)) -> 
      transformTypePart tgt typ (fun _ -> getTypePart src typ)

  | Value(Check _) | Value(PrimitiveEdit _) -> typ

  | _ -> failwith $"transformType: Unsupported edit {formatEdit ed}"
  
 
opsCore @ refactorListOps |> List.fold transformType (TyRecord("div", []))
opsCore @ opsBudget |> List.fold transformType (TyRecord("div", []))

*)
// --------------------------------------------------------------------------------------
// Effect analysis
// --------------------------------------------------------------------------------------

let doc1 = Apply.applyHistory (rcd "div") (opsBaseCounter @ opsCounterInc)
let evalOps = Eval.evaluateAll doc1

Merge.mergeHistories Merge.RemoveConflicting 
  (opsBaseCounter @ opsCounterInc @ opsCounterInc) 
  (opsBaseCounter @ opsCounterInc @ evalOps)

let e1s = (*opsBaseCounter @ opsCounterInc @*) opsCounterInc
let e2s = (*opsBaseCounter @ opsCounterInc @*) evalOps



let getDependencies ed = 
  match ed.Kind with 
  | Copy(_, _, src) ->  getTargetSelector ed :: src :: ed.Dependencies
  | _ -> getTargetSelector ed :: ed.Dependencies
  
let filterConflicting = 
  List.filterWithState (fun modsels ed ->
    printfn "MODSELS:\n * %s" (String.concat "\n * " (List.map formatSelector modsels))
    // Conflict if any dependency depends on any of the modified locations
    let conflict = getDependencies ed |> List.exists (fun dep -> 
      List.exists (fun modsel -> prefix dep modsel || prefix modsel dep) modsels)
    if conflict then false, (getTargetSelector ed)::modsels
    else true, modsels) 


let e1ModSels = e1s |> List.map getTargetSelector
filterConflicting e1ModSels e2s





type Effect = 
  | TagEffect
  | ValueEffect 
  | StructureEffect 

let getEffect ed = 
  match ed.Kind with 
  // Edits that can affect references in document
  | RecordRenameField(UpdateReferences, _, _, _)
  | Copy(UpdateReferences, _, _)
  | WrapRecord(UpdateReferences, _, _, _)
  | WrapList(UpdateReferences, _, _, _)
  | RecordDelete(UpdateReferences, _, _) -> StructureEffect, getTargetSelector ed
  // Edits that cannot affect references in document
  | UpdateTag _ -> TagEffect, getTargetSelector ed
  | _ -> ValueEffect, getTargetSelector ed


let conflicts (ef1kind, ef1sel) (ef2kind, ef2sel) = 
  ef1kind = ef2kind && (prefix ef1sel ef2sel || prefix ef2sel ef1sel)

let conflictsWith ef effs = 
  List.exists (conflicts ef) effs

let getEffects eds =
  set (List.map getEffect eds)

let formatEffect (kind, sels) = 
  sprintf 
    ( match kind with 
      | ValueEffect -> "val(%s)" 
      | TagEffect -> "tag(%s)"
      | StructureEffect -> "struct(%s)") (formatSelector sels)
(*
let getDependencies ed = 
  match ed.Kind with 
  | Shared(_, ListAppendFrom(_, src)) 
  | Shared(_, Copy(_, src)) -> src :: ed.Dependencies
  | _ -> ed.Dependencies
*)
let addEffect effs (kindEff, selEff) = 
  List.exists (fun (k, e) -> prefixGeneral true false e selEff)

//let e1s = addSpeakerOps
//let e2s = refactorListOps
let e1eff = getEffects e1s
let e2eff = getEffects e2s
for e in e1s do printfn $"{formatEdit e}\n  ({formatEffect (getEffect e)})\n"
for e in e2s do printfn $"{formatEdit e}\n  ({formatEffect (getEffect e)})\n"

printfn "E1 EFFECTS"
for eff in e1eff do printfn $"  {fst eff} {formatSelector (snd eff)}"
printfn "E2 EFFECTS"
for eff in e2eff do printfn $"  {fst eff} {formatSelector (snd eff)}"

(*
let e1s = addSpeakerOps
for e1 in e1s do printfn $"{formatEdit e1}"

let e2s = refactorListOps
for e2 in e2s do printfn $"{formatEdit e2}"

for e1 in e1s do
  printf "----------------------------------------------------------"
  for e2 in e2s do
    printfn $"\nCONFLICT CHECK"
    printfn $"  e1 = {formatEdit e1}"
    printfn $"  e1.target = {formatSelector(getTargetSelector e1)}"
    printfn $"  e2 = {formatEdit e2}"
    printfn $"  e2.target = {formatSelector(getTargetSelector e2)}"
*)


let doc = applyHistory (rcd "div") (opsCore @ opsBudget)
let evalEds = doc |> Eval.evaluateAll
let evalDoc = applyHistory (rcd "div") (opsCore @ opsBudget @ evalEds)

let formatEffect = function
  | ValueEffect tgt -> $"value of {formatSelector tgt}"
  | StructureEffect tgt -> $"structure of {formatSelector tgt}"
 
let filterConflicting modsels eds = 
  let rec loop acc modsels = function 
    | [] -> List.rev acc
    | ed::eds ->
        // Conflict if any dependency depends on any of the modified locations
        let conflict = getDependencies ed |> List.exists (fun dep -> 
          List.exists (fun modsel -> includes dep modsel) modsels)
        if conflict then loop acc ((getTargetSelector ed)::modsels) eds
        else loop (ed::acc) modsels eds
  loop [] modsels eds


let evalEdsFilter = filterConflicting [!/"/speakers"] evalEds

for e in evalEds do
  printfn $""
  printfn $"  * {formatEdit e}"
  printfn $"    modifies: {formatEffect (getEffect e)}"
  printfn $"    depends: {List.map formatSelector (getDependencies e)}"

for e in evalEdsFilter do
  printfn $""
  printfn $"  * {formatEdit e}"
  printfn $"    modifies: {formatEffect (getEffect e)}"
  printfn $"    depends: {List.map formatSelector (getDependencies e)}"


for e in addSpeakerOps do 
  printfn $""
  printfn $"  * {formatEdit e}"
  printfn $"    modifies: {formatEffect (getEffect e)}"
  printfn $"    depends: {List.map formatSelector (getDependencies e)}"

doc |> select !/"/ultimate/item"
evalDoc |> select !/"/ultimate/item/result"