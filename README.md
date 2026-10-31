# TextRenderer

A from-scratch text-to-image HTTP service in C#/.NET 10, built with Claude Code to show how I work with AI. It has its own glyph layout, rasteriser and PNG encoder.

- **Tested:** 358 tests, including pixel comparison against SkiaSharp
- **Load tested:** 17.3 million requests with no server errors
- **Plans and evidence:** [Part 1](PLAN_PART_1.md), [Part 2](PLAN_PART_2.md), [load results](LOAD_TEST_RESULTS.md)

## How to Run

Start the service:

```bash
dotnet run --project TextRenderer
```

Then open <http://localhost:5133/swagger> to view the API and render an image from the browser.

Run the tests:

```bash
dotnet test
```

## The Purpose

This repo is an exercise in AI development, where I have gone from no specific text-to-image rendering knowledge to building a fully functional service.
The task was taken from an example AI interview and is given at the bottom of this document. This project was not done in a standard interview timeframe as it was more important to show a proper planning process and to be kept in the loop in any decision making.

## Usage of AI and Example Prompts.
I used Claude with Opus 5.5. In short the process was to gain an understanding of the problem domain, then devise a plan, then execute the plan in stages, whilst being kept in the loop within each step, then reviewing code changes before committing.

### Initial Querying
`> Summarise the README and provide explanations for any JARGON`

`> Lets focus on Job 1. Explain an example from end to end of how the text is processed into an image`

### Planning Stage
Now it's time to start planning by bouncing off the AI and drive it into fleshing out a full plan based off the conversation.

`> plan only: What library could be used for the font parsing?`

`> PLAN only: lets go with sixLabors.Fonts and abstract it behind IFontSource. We will need classes Glyphs, a renderer class, a rasteriser class and an image encoder class to get the core functionality working. Is that all?`

`> On the design decisions: How big of a change is #1, can we do it later or will it be much harder to add it after the fact? #2 yes. #3 Let's rasterise per glyph`

`> Unit tests at each stage should be simple enough and should be added and passed before moving on. How can we test for correctness of the final image? Is it good enough for me to do it by eye`

The AI suggested using another library as an oracle to test against, which was a good idea. It paid of checking if there was another way.

## Implementation
Now it is time to start development. At this stage we go through the plan whilst I steer the AI into completing the task the way I want.

`> Lets start step 2. Keep me in the loop and ask before making any decisions not in the plan`

Don't let Claude go on it's own path. It also helps keep you informed piece meal on the changes being made.

`> Could you explain each of the none trivial assertions on the new test cases`

As this is a new problem domain for me, I wanted to check the more complicated test cases before committing them.

`> Could you also add unit tests using the other fonts as well`

`> can we add tests with "the quick brown fox etc" and numbers and characters so we can see a wider range of results`

This catches missing coverage and gets Claude to quickly make the change.

## Planning Part 2

`> nice. Let's plan part two. We will need to expose the service over http with a POST/render and a GET/fonts endpoint. We will need validation on inputs to only allow accepted characters. We will want to create integration tests / load tests / concurrency and make sure the service is stable. Is there anything I've missed?`

`> Downgrade to 2.x`

Claude flagged that SixLabors.Fonts needs a licence key for Release builds, so I chose to downgrade. When it tried this, it found 2.2.0 has the same check, and 2.1.3 only skips the check while keeping the same licence and needing fragile workarounds. We reverted to 3.2 based on that evidence. It's worth being willing to change a decision once you have the facts.

## Implementation

`> I can't see any tests testing concurrency`

Claude had reported the limit and timeout work as tested, but those tests only used a fake font. Nothing ran the real renderer in parallel. Pushing back led to tests that compare parallel output byte for byte against sequential output, and a deliberately added race showed they catch real bugs. Don't take "done" on trust.

` > Start step 2. Be concise with testing whilst keeping good coverage. Our test suite is starting to become bloated.`

It's great to have lots of tests, but we don't want to add tonnes of redundant tests as it's hard to keep track of the contract. Claude will add the same tests in different locations if given the chance.

## Clean up
`> plan only (potential cleanup) I'm worried the codebase might have some redundant tests. Do you have suggestions for simplifying the test suite?`

Claude can very quickly find similar tests. We don't want it jumping in and deleting testing without looking over what it finds first. In this case it found a lot of tests that were doubled up. 

## The task

You have two jobs.

1. **Build the core and get it working.** Read a font file, take an input string, and lay the glyphs out one after another using each glyph's advance width so the spacing looks right. There are a few TTFs in the `fonts/` folder to render, or bring your own. Rasterize the glyphs onto a canvas and write the result out as an image. The output format is your call — PNG, PPM, even a plain bitmap is fine, as long as it opens and shows the text. You're welcome to use a library to *parse* the font and pull out glyph outlines and metrics, but turning those outlines into pixels and placing the glyphs is the exercise — a one-call text renderer, or a library that hands you finished glyph bitmaps, is off-limits. You don't need to parse the font's binary tables by hand either; that part's fine to delegate.
2. **Make it a service.** Once it renders, internal teams are going to call it over HTTP, a lot, and lean on it. Take it as far toward something you'd be comfortable putting in front of them as you can. What that takes is yours to figure out. You won't get to everything in the time you have, and we don't expect you to. Be ready to walk through what you tackled first, what you left out, and why.
  One thing that's *not* in scope is deployment. We're not hosting this anywhere, so skip Dockerfiles, CI, and infra. The process running on your laptop is the service.