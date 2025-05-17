# Scribal Feature Checklist

## RAG and Indexing

- [ ] Define what gets indexed (chapters, character files, plot outline, premise, user notes)
- [ ] Define how retrieval is triggered (automatically during drafting/editing, explicit user query)
- [ ] Store vectors in `/.scribal/vectors/`
- [ ] Allow users to specify other folders for RAG ingestion
- [ ] Implement `scribal ingest` command to manage RAG data
- [ ] Implement functionality to report workspace status, potentially including RAG status

## Initialisation (`scribal init`)

- [x] Implement `scribal init` command
- [x] `scribal init`: Create `.scribal/` directory
- [x] `scribal init`: Create `config.json` with defaults and prompts
- [x] `scribal init`: Create an empty `project_state.json`
- [ ] `scribal init`: Create an empty `plot_outline.md` template
- [ ] `scribal init`: Create `characters/` directory
- [ ] `scribal init`: Create `chapters/` directory
- [x] `scribal init`: Set project state to "Initialized" in `project_state.json`
- [x] `project_state.json`: Track chapters and their individual states (e.g., unstarted, drafted, locked off)

## Pitch

- [x] Allow user to supply a core seed idea (pitch)
- [x] AI transforms pitch into a more substantial premise via a back-and-forth interaction
- [ ] Store the generated premise as a separate file
- [ ] Implement `scribal premise revise` command to update the premise
- [x] Use the premise to generate the initial plot overview

## Outline

- [x] Generate a high-level breakdown of chapters (the outline)
- [ ] Use the outline as seed material for generating chapter content
- [ ] Implement `scribal draft` command to regenerate chapters from the outline
- [ ] Implement `scribal check` command to compare a chapter with its outline section
- [ ] Allow user to decide which is 'correct' (chapter content or outline) and auto-adjust the other
- [ ] Outline: Each chapter entry includes a two-sentence summary
- [ ] Outline: Each chapter entry includes a detailed bullet-point breakdown of beats
- [ ] Outline: Each chapter entry includes a desired word count (optional)
- [ ] Outline: Each chapter entry includes a list of appearing characters (for RAG)

## User Choice and Interaction

- [x] Before AI edits are saved to the filesystem, prompt user with yes/no/refine options
- [x] 'Refine' option: Enter a back-and-forth chat with the model to iterate on its work

## Chapter Mode

- [x] Implement a tree view menu for chapter selection
- [x] Chapter menu: Include an option for creating a 'new' chapter
- [x] New chapter: Allow user to specify its ordinal position
- [x] New chapter: Allow user to specify its title
- [x] New chapter: Allow user to optionally provide an initial draft
- [x] New chapter: Allow user to optionally let the AI generate an initial version
- [x] Existing chapter: Display its current state
- [x] Existing chapter: Allow deletion
- [ ] Existing chapter: Allow splitting into multiple chapters
- [ ] Existing chapter: Allow merging with other chapters
- [ ] Chapter splitting: Prompt user for updated descriptions for the source and new chapter(s); create new chapter(s)
  empty for manual content transfer
- [ ] Chapter merging: Prompt user for a description of the new merged chapter; concatenate file contents
- [x] Deleting a chapter: Automatically update the ordinals of subsequent chapters

## Drafting

- [x] When in chapter mode, provide options for drafting content
- [x] Drafting: Option to write to a new file (e.g., `chapter_01_draftN.md`)
- [ ] Drafting: Option to overwrite the existing chapter file
- [ ] Ensure the latest draft takes priority or is clearly marked
- [x] If a git repository is present, make a git commit after any significant drafting action
- [ ] If a git repository is present and dirty when starting drafting, ask the user if they want to commit changes first

## Edit Mode

- [ ] Implement an 'edit' mode, similar to drafting
- [ ] In edit mode, the AI model responds to a user prompt instead of only the chapter summary from the outline
- [ ] When a chapter is changed (drafted or edited), update its summary in the main plot outline

## Characters

- [ ] Treat character definition files similarly to chapter files in terms of management
- [ ] Store character files in a dedicated `characters/` folder

## Check Commands (`scribal check`)

- [ ] Implement `scribal check chapters`: High-level command to iterate over all chapters, identifying discrepancies
  with the outline
- [ ] Implement `scribal check continuity`: Perform continuity review within a chapter or across chapters
- [ ] Implement `scribal check quality`: Check for spelling, grammar, and style issues
- [ ] Allow `scribal check` commands to operate on a specified range of chapters or all chapters
- [ ] Consolidate and present results clearly when checking multiple chapters

## Export (`scribal export`)

- [ ] Implement `scribal export` command
- [ ] `scribal export`: Concatenate all final chapter versions (and potentially other selected content) into a single
  large Markdown file

## General CLI Features

- [x] Implement `scribal status` command: Display current project state, active pipeline stage, and status of each
  chapter
- [ ] Implement global `--dry-run` flag for commands that modify files
- [ ] Implement global `--yes` flag to auto-confirm prompts
- [ ] Differentiate behavior for 'workspace' mode (when a `.scribal` folder is found) versus 'headless' mode
- [ ] Ensure no workspace-specific features are available or attempted in headless mode

## File Structure and Configuration (from diagram)

- [ ] Ensure `my_story/.scribal/config.json` correctly stores AI provider settings, API keys (or paths to local models),
  and project preferences
- [ ] Ensure `my_story/.scribal/project_state.json` accurately tracks the current pipeline stage, status of files, and
  other metadata
- [ ] Ensure `my_story/.scribal/plot_outline.md` serves as the master document containing the Pitch, Synopsis, Character
  List references, and Chapter breakdown
- [ ] Ensure `my_story/.scribal/characters/` is the designated directory for character definition files
- [ ] Ensure `my_story/chapters/` is the designated directory for chapter text files (drafts, final versions)
- [ ] Consider how user-managed folders (e.g., `worldbuilding/`, `notes/`) are handled, possibly via RAG configuration
