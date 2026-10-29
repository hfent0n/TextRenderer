# glyph

glyph is a text-to-image render service you build from scratch. Internal teams hand it some text and a font file, and it draws that text in that font and hands back an image. Picture the path the characters travel: the font file holds the shape of every glyph along with how much space each one takes up, your code reads those shapes, lays the characters out left to right so they don't pile on top of each other, paints them onto a canvas, and encodes the canvas as an image you can open and look at.

## Your task

You have two jobs.

1. **Build the core and get it working.** Read a font file, take an input string, and lay the glyphs out one after another using each glyph's advance width so the spacing looks right. There are a few TTFs in the `fonts/` folder to render, or bring your own. Rasterize the glyphs onto a canvas and write the result out as an image. The output format is your call — PNG, PPM, even a plain bitmap is fine, as long as it opens and shows the text. You're welcome to use a library to *parse* the font and pull out glyph outlines and metrics, but turning those outlines into pixels and placing the glyphs is the exercise — a one-call text renderer, or a library that hands you finished glyph bitmaps, is off-limits. You don't need to parse the font's binary tables by hand either; that part's fine to delegate.
2. **Make it a service.** Once it renders, internal teams are going to call it over HTTP, a lot, and lean on it. Take it as far toward something you'd be comfortable putting in front of them as you can. What that takes is yours to figure out. You won't get to everything in the time you have, and we don't expect you to. Be ready to walk through what you tackled first, what you left out, and why.
  One thing that's *not* in scope is deployment. We're not hosting this anywhere, so skip Dockerfiles, CI, and infra. The process running on your laptop is the service.

We care more about your judgment on what to tackle first than about a long list of half-finished features.

## Time

Aim for **60 minutes**. This is practice, so going over is fine, but your submission records exactly how long you took, and that time factors into the evaluation.

## Tools

Use whatever you'd use day to day. AI assistants (Cursor, Copilot, ChatGPT, Claude), search, library docs, Stack Overflow. Anything goes. We want to see how you actually work, not how you perform without your normal tooling.

## Building it

This repo starts nearly empty — just this README and a `fonts/` folder with a few TTFs to render — so you're starting from close to a blank page. Set it up in whatever language and framework you like, using your own toolchain (your own package manager, test runner, and build commands).

When you're done, run `npx @hellointerview/ai-coding submit` to submit your code.

## Git

This is a regular git repo, so use git however you like. Commit as you go, make branches, do whatever fits how you normally work. It won't get in the way and we're not going to fight your workflow.

The one thing to know is there's nowhere to push. When you run `npx @hellointerview/ai-coding submit` we bundle up your work and submit it for you, so commits are just for your own benefit, not how you turn the exercise in. Your full set of changes is included either way, whether you committed them or not.
