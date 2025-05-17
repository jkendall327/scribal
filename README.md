# Scribal

![](docs/image.webp)

AI-powered command-line fiction writing agent for drafting and revising prose. It scans your project's markdown files to
understand your story's structure, characters, and plot, then provides contextually relevant assistance.

Made because aider isn't really meant for fiction and its repo-map feature doesn't include Markdown files.

## Features

- Analyzes your project's structure via Markdown parsing.
- Bring your own API key.
- Turns an elevator pitch for a story into a full first draft.
- Chapter management system: iterative drafting and re-drafting, deletion, splitting, merging.
- Git integration: commits made automatically so models can never haphazardly delete your work.

## Getting started

- Download the app from [the most recent release](https://github.com/jkendall327/scribal/releases).
- Extract the .zip.
- Set your preferred models and API keys (see 'Model configuration' below).
- Add Scribal to your system PATH.
- If you want to create a full story from scratch:
    - Open a new directory and run Scribal.
    - Use the `/init` command to create a workspace.
    - Use the `/pitch` command to create a premise from your elevator pitch.
    - Use the `/outline` command to turn that pitch into a full chapter-by-chapter outline.
    - Use the `/chapters` command to start drafting or revising content on a per-chapter basis.
- If you want to use it on existing material:
    - Navigate to the folder containing your story.
    - Run Scribal.
    - Just start talking to the model.
    - You can ask it questions (`/ask where do I introduce the antagonist again?`)
    - You can tell it to edit files (`edit the ending of chapter 3 to be in Spanish, please`)

### Model configuration

Scribal uses AI models in three ways.

- The *main model* is used for actually producing and editing prose.
- The *weak model* is used for simpler, less-important tasks, like generating Git commit messages.
- The *embeddings model* is used to generate vector embeddings of your content for RAG.

You can configure each of these independently.

When you download Scribal, open the `appsettings.json` file to find slots for each of these model types.

```json
{
  "AI": {
    "Primary": {
      "Provider": "gemini",
      "ModelId": "gemini-2.5-pro",
      "ApiKey": "[...]"
    },
    "Weak": {
      "Provider": "openai",
      "ModelId": "gpt-4o-mini",
      "ApiKey": "[...]"
    },
    "Embeddings": {
      "Provider": "openai",
      "ModelId": "embedding-3-small",
      "ApiKey": "[...]"
    }
  }
}
```

You can use different providers and models for each of these slots.

Scribal relies on .NET's native `IConfiguration` system. That means you have a lot of flexibility in how to set your config.

You can freely mix settings between the `appsettings.json` file, environment variables and command-line arguments.

You can, for instance, put the model names in the .json file but specify API keys in environment variables.

Note that for environment variables, you use two underscores to represent hierarchy:

Windows (Powershell):

`$env:AI__Embeddings__ModelId = "some-other-embedding-model"`

Linux:

`export AI__Primary__ApiKey=12345`

On the command line, it's a lot easier:

`scribal --AI:Weak:Provider="gemini"`

## Other configuration

Scribal also respects these options:

```json
{
  "AppConfig": {
    "DryRun": "false",
    "IngestContent": "false"
  }
}
```

- `DryRun`: most actions which touch the filesystem will not actually occur. Useful for testing.
- `IngestContent`: toggles the WIP feature for RAG. Will probably break if enabled.

## Context

Scribal recursively scans the working directory for all Markdown files.

It then provides the model with a high-level map of their names and place in the filesystem.

Use the `/tree` command can select files to send to the model in full, if you know they are important.

When in a free chat (i.e. not executing a `/command`), the model also has the ability to directly read files from the filesystem.

It is only allowed to read content from within the current working directory.

I am planning to add proper RAG (retrieval augmented generation) capabilities in the future, but this is not yet
implemented.

## Limitations

Right now, Scribal only recognises Markdown (`.md`) files.

The following AI providers are supported:

- OpenAI
- Gemini
- Anthropic
- DeepSeek

I plan to support local models soon.

## Credit

Scribal is indebted to [Aider](https://github.com/paul-gauthier/aider), by Paul Gauthier.

## License

MIT.
