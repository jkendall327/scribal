## TODO
  Plan: Define what gets indexed: chapters (latest? final?), character files, plot outline, premise, maybe user notes? How is retrieval triggered? Automatically during drafting/editing based on the current task? Via explicit user query? The /.scribal/vectors/ location seems sensible.
- allow users to specify other folders that get sucked into RAG?
- explicit 'scribal ingest' command to avoid complexity of a background service? just for now?
- https://github.com/microsoft/semantic-kernel/blob/main/dotnet/samples/Demos/VectorStoreRAG/DataLoader.cs

should report workspace status?

background service to handle ingestion concerns

## Initialisation
- 'scribal init'
- Creates the .scribal/ directory, config.json (with defaults/prompts), an empty project_state.json, an empty plot_outline.md template, and the characters/ and chapters/ directories. Sets project state to "Initialized".
- The state.json tracks chapters and their individual state (unstarted, drafted, locked off...)

## Pitch
- The core seed idea supplied by the user
- Transformed by the AI into a more substantial premise via a back-and-forth
- Premise gets stored as its own file
- Can be updated ('scribal premise revise')
- Used to generate the initial plot overview, not really used afterward unless you want to start over

## Outline
- High-level breakdown of the chapters
- Used as the seed material for the content of the chapters
- Chapters can be regen'd from the outline at any time ('scribal draft')
- Can manually check if a chapter matches the draft ('scribal check'), which lets you decide which is 'right' and auto-adjust the other
- Each chapter has both a two-sentence summary and a more detailed bulletpoint breakdown of beats
- And a desired wordcount?
- And a list of appearing characters, so RAG can be used to pull in their details.

## User choice
- Whenever the AI makes an edit, before it's reified into the filesystem...
- The user can choose from a yes/no/refine prompt
- Where 'refine' enters into a back-and-forth chat with the model iterating on its work

## Chapter mode
- Tree view menu where you select chapters
- with an option for 'new'
- new chapter: state its ordinal position, its title, optionally provide a draft, optionally let the ai have an initial go at it
- existing chapter: see state, allow deletion, splitting, merging
- how to handle splitting sensibly? ask the user to provide an updated description for the source, and one for the new chapter. create the new chapter but leave it empty; let them move the content they want
- for merging, get a description of the new chapter and just copy-paste file contents in
- deleting a chapter is simple, just update the ordinals of everything else.

## Drafting
- When in chapter mode
- Gives user option to write to a new file (chapter 1-draft1.md?) or to overwrite the existing
- latest draft takes priority
- make a git commit after any action anyway
- if git repo is dirty when starting drafting, ask user if they want to commit first

## Edit
- Same as drafting, except the model is responding to a user prompt instead of the chapter summary found in the overview
- Whenever a chapter is changed, it updates the chapter summary in the outline

## Characters
- Characters are essentially just chapters?
- Except held in a different folder?

## Check
- 'scribal check' family of commands
- 'scribal check chapters': High-level command that iterates over all chapters, identifying discrepencies with the outline
- 'scribal check continuity': continuity review within a chapter?
- 'scribal check quality': spelling, grammar
- maybe all of these can be on a chapter range or a for-loop over all chapters, if the user selects it
- results are done for each chapter and then concat'd

## Export
- Concat everything to one big Markdown file, etc
- 'scribal export'

## Other

scribal status: Displays the current project state: active pipeline stage, status of each chapter

Flags: `--dry-run`, `--yes`.

---

if a .scribal folder is found => in 'workspace' mode
otherwise => in 'headless' mode
no workspace features available in headless

---

```
my_story/
├── .scribal/
│   ├── config.json             # AI provider settings, API keys (or path to local model), project prefs
│   ├── project_state.json      # Tracks current pipeline stage, file statuses, metadata
│   ├── plot_outline.md         # Master document: Pitch, Synopsis, Character List refs, Chapter breakdown
│---characters/             # Directory for character definition files
│       ├── main_character.md
│       └── antagonist.md
├── chapters/                   # Directory for chapter text files
│   ├── chapter_01_draft1.md
│   ├── chapter_01_draft2.md
│   ├── chapter_01_final.md
│   ├── chapter_02_draft1.md
│   └── ...
└── (optional: worldbuilding/, notes/, etc.) # User-managed folders
```

