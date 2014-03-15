# Gerty

An asynchronous, non-blocking, redis-powered queue worker written in C#.


## About

Gerty is a library that asynchronously consumes a job queue hosted on redis, dispatching work items to custom handlers that you supply.


### Goals

I had a few goals in mind when I set out to create Gerty:

 - Mono and Linux compatibile
 - Asynchronous, non-blocking and performant like node.js
 - Unlike node.js however, cleaner and more explicit code
 - Reentrant when individual jobs fail
 - Stable and clear handling of error conditions
 - Simple to integrate with other systems, no AMQP
 - Low operational and maintenance overhead
 - Reasonable duplicate work detection


### Technical

Gerty leverages Redis' single threaded nature along with C# async/await mechanics to complete jobs as quickly as it can.  Based on your architecture (multi-machine, multi-core), multiple instances of Gerty are also able to be pointed at the same namespace to handle more jobs at once.

I've chosen what I feel are the most pervasive tools for each concern, as a result Gerty depends on the following libraries:

 - [Booksleeve](https://code.google.com/p/booksleeve)
 - [JSON.net](http://james.newtonking.com/json)
 - [NLog](http://nlog-project.org)

If you feel like any of these can or should be abstracted away or has a superior alternative, feel free to let me know or send a pull request!


## Using

I am in the process of getting Gerty set up as a NuGet package; Currently there is a [known issue with creating packages from Linux](https://nuget.codeplex.com/workitem/4077) which I would like to see addressed.  This shouldn't however prevent you from checking out the code and building it yourself in the meantime, and once the nuget package is published, adding it to your project.

I also encourage you to vote for that issue and leave a comment until it is resolved.


## Meta

### License

This project is available under the Apache 2.0 license.


### Contributing

If you're interested in making contributions to this project, feel free to send me a pull request.  Don't forget to add yourself to the [contributors.md](/contributors.md) file!


### Credits

Like what you see?  Have a question or suggestion?  Feel free to get in touch, either via github or my profile.

Gerty is created by [Alexander Trauzzi](http://profiles.google.com/atrauzzi)