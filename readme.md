# Gerty

An asynchronous, non-blocking, redis-powered queue worker written in C#.


## About

Gerty is a library that asynchronously consumes a job queue hosted on redis, dispatching work items to custom handlers that you supply.


### Goals

I had a few goals in mind when I set out to create Gerty:

 - Mono and Linux compatibile
 - Asynchronous, non-blocking and performant like node.js
 - Unlike node.js however, cleaner and more explicit code
 - Stable and clear handling of error conditions
 - Simple to integrate with other systems, no AMQP
 - Low operational and maintenance overhead


### Technical

More details to come!

## Using

Stay tuned! I'm in the process of getting Gerty set up as a NuGet package.


## Meta

### License

This project is available under the Apache 2.0 license.


### Contributing

If you're interested in making contributions to this project, feel free to send me a pull request.  Don't forget to add yourself to the [contributors.md](/contributors.md) file!


### Credits

Like what you see?  Have a question or suggestion?

Gerty is created by [Alexander Trauzzi](http://profiles.google.com/atrauzzi)