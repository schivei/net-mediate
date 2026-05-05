import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        <Heading as="h1" className="hero__title">
          {siteConfig.title}
        </Heading>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link
            className="button button--secondary button--lg"
            to="/docs/intro">
            Get Started - 5min ⏱️
          </Link>
          <Link
            className="button button--outline button--secondary button--lg"
            to="/docs/getting-started/installation"
            style={{marginLeft: '1rem'}}>
            Installation Guide
          </Link>
        </div>
        <div className={styles.badges}>
          <a href="https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml">
            <img src="https://github.com/schivei/net-mediate/actions/workflows/ci-cd.yml/badge.svg" alt="CI/CD Pipeline" />
          </a>
          <a href="https://www.nuget.org/packages/NetMediate/">
            <img src="https://img.shields.io/nuget/v/NetMediate?style=flat" alt="NuGet" />
          </a>
        </div>
      </div>
    </header>
  );
}

const FeatureList = [
  {
    title: 'Easy to Use',
    emoji: '🚀',
    description: (
      <>
        NetMediate is designed to be simple and intuitive. Define your messages
        and handlers, register them with dependency injection, and start using
        the mediator pattern immediately.
      </>
    ),
  },
  {
    title: 'Type-Safe',
    emoji: '🔒',
    description: (
      <>
        Built with strong typing in mind. Commands, requests, notifications, and
        streams are all fully typed, providing compile-time safety and excellent
        IDE support.
      </>
    ),
  },
  {
    title: 'AOT Compatible',
    emoji: '⚡',
    description: (
      <>
        Full support for .NET Native AOT compilation with source generators.
        No reflection at runtime means blazing-fast performance and small
        deployment sizes.
      </>
    ),
  },
  {
    title: 'Pipeline Behaviors',
    emoji: '🔄',
    description: (
      <>
        Implement cross-cutting concerns like logging, validation, and caching
        with pipeline behaviors. Wrap your handlers with reusable middleware-style
        interceptors.
      </>
    ),
  },
  {
    title: 'Built-in Resilience',
    emoji: '🛡️',
    description: (
      <>
        Optional resilience package provides retry, timeout, and circuit breaker
        behaviors out of the box. Make your applications more robust with minimal
        configuration.
      </>
    ),
  },
  {
    title: 'Observability Ready',
    emoji: '📊',
    description: (
      <>
        Native OpenTelemetry support with ActivitySource and Meter for traces
        and metrics. DataDog integration packages available for comprehensive
        observability.
      </>
    ),
  },
];

function Feature({emoji, title, description}) {
  return (
    <div className={clsx('col col--4')}>
      <div className="text--center">
        <div className={styles.featureEmoji}>{emoji}</div>
      </div>
      <div className="text--center padding-horiz--md">
        <Heading as="h3">{title}</Heading>
        <p>{description}</p>
      </div>
    </div>
  );
}

function HomepageFeatures() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className="row">
          {FeatureList.map((props, idx) => (
            <Feature key={idx} {...props} />
          ))}
        </div>
      </div>
    </section>
  );
}

function QuickExample() {
  return (
    <section className={styles.quickExample}>
      <div className="container">
        <div className="row">
          <div className="col">
            <Heading as="h2" className="text--center margin-bottom--lg">
              Quick Example
            </Heading>
            <div className="margin-bottom--lg">
              <pre>
                <code className="language-csharp">{`// 1. Install the packages
dotnet add package NetMediate
dotnet add package NetMediate.SourceGeneration

// 2. Define a notification
public record UserCreated(string UserId, string Email);

// 3. Create a handler
public class UserCreatedHandler : INotificationHandler<UserCreated>
{
    public Task Handle(UserCreated notification, CancellationToken ct)
    {
        Console.WriteLine($"User {notification.UserId} created!");
        return Task.CompletedTask;
    }
}

// 4. Register services
builder.Services.AddNetMediate();

// 5. Use the mediator
await mediator.Notify(new UserCreated("123", "user@example.com"));`}</code>
              </pre>
            </div>
            <div className="text--center">
              <Link
                className="button button--primary button--lg"
                to="/docs/getting-started/quick-start">
                View Full Quick Start Guide →
              </Link>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

export default function Home() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title={`${siteConfig.title} - ${siteConfig.tagline}`}
      description="A lightweight and efficient .NET implementation of the Mediator pattern for in-process messaging and communication between components.">
      <HomepageHeader />
      <main>
        <HomepageFeatures />
        <QuickExample />
      </main>
    </Layout>
  );
}
