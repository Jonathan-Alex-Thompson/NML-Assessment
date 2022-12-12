using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Nml.Improve.Me.Dependencies;

namespace Nml.Improve.Me
{
	public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
	{
		private readonly IDataContext DataContext;
		private IPathProvider _templatePathProvider;
		public IViewGenerator View_Generator;
		internal readonly IConfiguration _configuration;
		private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
		private readonly IPdfGenerator _pdfGenerator;

		public PdfApplicationDocumentGenerator(
			IDataContext dataContext,
			IPathProvider templatePathProvider,
			IViewGenerator viewGenerator,
			IConfiguration configuration,
			IPdfGenerator pdfGenerator,
			ILogger<PdfApplicationDocumentGenerator> logger)
		{
			if (dataContext != null)
				throw new ArgumentNullException(nameof(dataContext));
			
			DataContext = dataContext;
			_templatePathProvider = templatePathProvider ?? throw new ArgumentNullException("templatePathProvider");
			View_Generator = viewGenerator;
			_configuration = configuration;
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_pdfGenerator = pdfGenerator;
		}
		
		public byte[] Generate(Guid applicationId, string baseUri)
		{
			var application = DataContext.Applications.Single(app => app.Id == applicationId);

			if (application != null)
			{
				var view = GenerateViewString(application, baseUri);
				if (!string.IsNullOrWhiteSpace(view)){
					var pdfOptions = new PdfOptions
					{
						PageNumbers = PageNumbers.Numeric,
						HeaderOptions = new HeaderOptions
						{
							HeaderRepeat = HeaderRepeat.FirstPageOnly,
							HeaderHtml = PdfConstants.Header
						}
					};
					return _pdfGenerator.GenerateFromHtml(view, pdfOptions).ToBytes();
				}
				_logger.LogWarning(
					$"Unable to generate view.");
				return null;
			}

			_logger.LogWarning(
				$"No application found for id '{applicationId}'");
			return null;
		}

		private string GenerateViewString(Application application, string baseUri)
		{
			if (baseUri.EndsWith("/"))
				baseUri = baseUri.Substring(baseUri.Length - 1);
			
			switch (application.State)
			{
				case ApplicationState.Pending:
				{
					var path = _templatePathProvider.Get("PendingApplication");
					var vm = new PendingApplicationViewModel
					{
						ReferenceNumber = application.ReferenceNumber,
						State = GetDescription(application),
						FullName = GetFullName(application),
						AppliedOn = application.Date,
						SupportEmail = _configuration.SupportEmail,
						Signature = _configuration.Signature
					};
					
					return View_Generator.GenerateFromPath($"{baseUri}{path}", vm);
				}
				case ApplicationState.Activated:
				{
					var path = _templatePathProvider.Get("ActivatedApplication");
					var vm = new ActivatedApplicationViewModel
					{
						ReferenceNumber = application.ReferenceNumber,
						State = GetDescription(application),
						FullName = GetFullName(application),
						LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
						PortfolioFunds = GetPortfolioFunds(application),
						PortfolioTotalAmount = GetPortfolioTotalAmount(application),
						AppliedOn = application.Date,
						SupportEmail = _configuration.SupportEmail,
						Signature = _configuration.Signature
					};
					
					return View_Generator.GenerateFromPath($"{baseUri}{path}", vm);
				}
				case ApplicationState.InReview:
				{
					var path = _templatePathProvider.Get("InReviewApplication");
					var inReviewMessage = "Your application has been placed in review" +
					                      application.CurrentReview.Reason switch
					                      {
						                      { } reason when reason.Contains("address") =>
							                      " pending outstanding address verification for FICA purposes.",
						                      { } reason when reason.Contains("bank") =>
							                      " pending outstanding bank account verification.",
						                      _ =>
							                      " because of suspicious account behaviour. Please contact support ASAP."
					                      };
					
					var vm = new InReviewApplicationViewModel
					{
						ReferenceNumber = application.ReferenceNumber,
						State = GetDescription(application),
						FullName = GetFullName(application),
						LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
						PortfolioFunds = GetPortfolioFunds(application),
						PortfolioTotalAmount = GetPortfolioTotalAmount(application),
						InReviewMessage = inReviewMessage,
						InReviewInformation = application.CurrentReview,
						AppliedOn = application.Date,
						SupportEmail = _configuration.SupportEmail,
						Signature = _configuration.Signature
					};

					return View_Generator.GenerateFromPath($"{baseUri}{path}", vm);
				}
				default:
					_logger.LogWarning(
						$"The application is in state '{application.State}' and no valid document can be generated for it.");
					return null;
			}
		}

		private double GetPortfolioTotalAmount(Application application)
		{
			return application.Products.SelectMany(p => p.Funds)
				.Select(f => (f.Amount - f.Fees) * _configuration.TaxRate)
				.Sum();
		}
		
		private string GetDescription(Application application)
		{
			return application.State.ToDescription();
		}
		
		private string GetFullName(Application application)
		{
			return $"{application.Person.FirstName} {application.Person.Surname}";
		}

		private IEnumerable<Fund> GetPortfolioFunds(Application application)
		{
			return application.Products.SelectMany(p => p.Funds);
		}
	}
}
