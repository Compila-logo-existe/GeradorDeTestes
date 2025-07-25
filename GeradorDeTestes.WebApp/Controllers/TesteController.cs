using GeradorDeTestes.Dominio.ModuloDisciplina;
using GeradorDeTestes.Dominio.ModuloMateria;
using GeradorDeTestes.Dominio.ModuloQuestao;
using GeradorDeTestes.Dominio.ModuloTeste;
using GeradorDeTestes.Infraestrutura.ORM.Compartilhado;
using GeradorDeTestes.WebApp.Extensions;
using GeradorDeTestes.WebApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeradorDeTestes.WebApp.Controllers;

[Route("testes")]
public class TesteController : Controller
{
    private readonly GeradorDeTestesDbContext contexto;
    private readonly GeradorPdfService geradorPdfService;
    private readonly IRepositorioDisciplina repositorioDisciplina;
    private readonly IRepositorioMateria repositorioMateria;
    private readonly IRepositorioQuestao repositorioQuestao;
    private readonly IRepositorioTeste repositorioTeste;

    public TesteController(GeradorDeTestesDbContext contexto, GeradorPdfService geradorPdfService,
        IRepositorioDisciplina repositorioDisciplina, IRepositorioMateria repositorioMateria,
        IRepositorioQuestao repositorioQuestao, IRepositorioTeste repositorioTeste)
    {
        this.contexto = contexto;
        this.geradorPdfService = geradorPdfService;
        this.repositorioDisciplina = repositorioDisciplina;
        this.repositorioMateria = repositorioMateria;
        this.repositorioQuestao = repositorioQuestao;
        this.repositorioTeste = repositorioTeste;
    }

    public IActionResult Index()
    {
        List<Teste> testesNaoFinalizados = repositorioTeste.SelecionarNaoFinalizados();

        repositorioTeste.RemoverRegistros(testesNaoFinalizados);

        List<Teste> testes = repositorioTeste.SelecionarRegistros()
                                             .Where(t => t.Finalizado)
                                             .ToList();

        VisualizarTestesViewModel visualizarVM = new(testes);

        return View(visualizarVM);
    }

    [HttpGet("cadastrar")]
    public IActionResult Cadastrar()
    {
        List<Disciplina> disciplinas = repositorioDisciplina.SelecionarRegistros();

        CadastrarTesteViewModel cadastrarVM = new(disciplinas);

        return View(cadastrarVM);
    }

    [HttpPost("cadastrar")]
    public IActionResult Cadastrar(CadastrarTesteViewModel cadastrarVM)
    {
        List<Disciplina> disciplinas = repositorioDisciplina.SelecionarRegistros();

        if (repositorioTeste.SelecionarRegistros().Any(t => t.Titulo.Equals(cadastrarVM.Titulo)))
            ModelState.AddModelError("ConflitoCadastro", "Já existe um teste com este título.");

        if (!ModelState.IsValid)
        {
            cadastrarVM.Disciplinas = disciplinas.Select(d => new SelectListItem
            {
                Text = d.Nome,
                Value = d.Id.ToString()
            }).ToList();

            return View(cadastrarVM);
        }

        Disciplina disciplina = disciplinas.FirstOrDefault(d => d.Id.Equals(cadastrarVM.DisciplinaId))!;

        Teste novoTeste = cadastrarVM.ParaEntidade(disciplina);

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioTeste.CadastrarRegistro(novoTeste);

            contexto.Testes.Add(novoTeste);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        string tipoGeracao = cadastrarVM.EhProvao ? nameof(GerarProvao) : nameof(GerarTeste);

        return RedirectToAction(tipoGeracao, new { id = novoTeste.Id });
    }

    [HttpGet("gerar-teste")]
    public IActionResult GerarTeste(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        List<Materia> materiasSelecionadas = testeSelecionado.Materias;

        List<Materia> materias = repositorioMateria.SelecionarRegistros().Where(m => m.Disciplina.Equals(testeSelecionado.Disciplina))
            .Where(m => m.Serie.Equals(testeSelecionado.Serie))
            .ToList();

        List<Questao> questoes = repositorioQuestao.SelecionarRegistros();

        testeSelecionado.Questoes.Clear();

        foreach (TesteMateriaQuantidade q in testeSelecionado.QuantidadesPorMateria)
        {
            List<Questao> questoesDaMateria = questoes
                .Where(questao => questao.Materia.Id.Equals(q.Materia.Id))
                .Where(q => q.Finalizado)
                .Take(q.QuantidadeQuestoes)
                .ToList();

            foreach (Questao questao in questoesDaMateria)
            {
                testeSelecionado.AderirQuestao(questao);
            }
        }

        if (TempData["Embaralhar"] is not null)
        {
            testeSelecionado.Questoes.Clear();
            contexto.QuantidadesPorMateria.RemoveRange(testeSelecionado.QuantidadesPorMateria);
            contexto.SaveChanges();

            if (testeSelecionado.Questoes.Count < testeSelecionado.QuantidadeQuestoes)
            {
                List<Questao> todasQuestoes = new List<Questao>();

                foreach (Materia materia in materiasSelecionadas)
                {
                    List<Questao> questoesDaMateria = questoes
                        .Where(q => q.Materia.Id.Equals(materia.Id) && q.Finalizado)
                        .Take(testeSelecionado.QuantidadeQuestoes)
                        .ToList();

                    todasQuestoes.AddRange(questoesDaMateria);
                }

                todasQuestoes.Shuffle();

                foreach (Questao questao in todasQuestoes.Take(testeSelecionado.QuantidadeQuestoes).ToList())
                {
                    if (testeSelecionado.Questoes.Any(q => q.Equals(questao)))
                        continue;

                    testeSelecionado.AderirQuestao(questao);
                    repositorioTeste.AtualizarQuantidadePorMateria(testeSelecionado, questao.Materia);
                }
            }
            contexto.SaveChanges();
            testeSelecionado.Questoes.Shuffle();
        }

        FormGerarPostViewModel gerarTestePostVM = testeSelecionado.ParaGerarTestePostVM(materias, materiasSelecionadas);

        return View(gerarTestePostVM);
    }

    [HttpPost("gerar-teste")]
    public IActionResult GerarTeste(Guid id, FormGerarPostViewModel gerarTestePostVM)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        if (gerarTestePostVM.QuestoesSelecionadasIds.Count < testeSelecionado.QuantidadeQuestoes)
            ModelState.AddModelError("ConflitoGeracao", "O número de questões selecionadas é menor do que o esperado.");

        if (!ModelState.IsValid)
        {
            List<Materia> materias = repositorioMateria.SelecionarRegistros().Where(m => m.Disciplina.Id.Equals(testeSelecionado.Disciplina.Id)
            && m.Serie.Equals(testeSelecionado.Serie)).ToList();

            FormGerarViewModel formGerarVM = testeSelecionado.ParaGerarTestePostVM(materias, testeSelecionado.Materias);

            return View(nameof(GerarTeste), formGerarVM);
        }

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            testeSelecionado.Finalizado = true;

            contexto.Update(testeSelecionado);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("/testes/{id:guid}/selecionar-materia/{materiaId:guid}")]
    public IActionResult SelecionarMateria(Guid id, Guid materiaId)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        Materia materiaSelecionada = repositorioMateria.SelecionarRegistroPorId(materiaId)!;

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            testeSelecionado.AderirMateria(materiaSelecionada);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(GerarTeste), new { id });
    }

    [HttpPost, Route("/testes/{id:guid}/remover-materia/{materiaId:guid}")]
    public IActionResult RemoverMateria(Guid id, Guid materiaId)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        Materia materiaSelecionada = repositorioMateria.SelecionarRegistroPorId(materiaId)!;

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            testeSelecionado.RemoverMateria(materiaSelecionada);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(GerarTeste), new { id });
    }

    [HttpGet, Route("/testes/{id:guid}/definir-quantidade-questoes/{materiaId:guid}")]
    public IActionResult DefinirQuantidadeQuestoes(Guid id, Guid materiaId)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        Materia materiaSelecionada = repositorioMateria.SelecionarRegistroPorId(materiaId)!;

        List<Questao> questoes = materiaSelecionada.Questoes.ToList();

        Disciplina disciplinaSelecionada = repositorioDisciplina.SelecionarRegistroPorId(testeSelecionado.Disciplina.Id)!;

        List<Materia> materias = repositorioMateria.SelecionarRegistros()
            .Where(m => m.Disciplina.Equals(disciplinaSelecionada) && m.Serie.Equals(testeSelecionado.Serie))
            .ToList();

        DefinirQuantidadeQuestoesViewModel vm = new()
        {
            Id = testeSelecionado.Id,
            Titulo = testeSelecionado.Titulo,
            NomeDisciplina = disciplinaSelecionada.Nome,
            Serie = testeSelecionado.Serie,
            MateriaId = materiaId,
            Questoes = questoes.Select(q => new SelectListItem()
            {
                Text = q.Enunciado,
                Value = q.Id.ToString()
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost, Route("/testes/{id:guid}/definir-quantidade-questoes/{materiaId:guid}")]
    public IActionResult DefinirQuantidadeQuestoes(Guid id, Guid materiaId, DefinirQuantidadeQuestoesPostViewModel vm)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        Materia materiaSelecionada = testeSelecionado.Materias.FirstOrDefault(m => m.Id.Equals(materiaId))!;

        TesteMateriaQuantidade? objComQuantidade = testeSelecionado.QuantidadesPorMateria
            .FirstOrDefault(x => x.Materia.Id.Equals(materiaId));

        int quantidadeTotalQuestoes = testeSelecionado.QuantidadesPorMateria.Sum(q => q.QuantidadeQuestoes);

        int quantidadeAnterior = objComQuantidade?.QuantidadeQuestoes ?? 0;

        int novaQuantidadeTotal = quantidadeTotalQuestoes - quantidadeAnterior + vm.QuantidadeQuestoesMateria;

        if (vm.QuantidadeQuestoesMateria > materiaSelecionada.Questoes.Count)
        {
            ModelState.AddModelError("ConflitoQuantidadeQuestoesMateria", $"A quantidade inserida é maior do que a quantidade de questões da matéria.");
        }
        else if (novaQuantidadeTotal > testeSelecionado.QuantidadeQuestoes)
        {
            ModelState.AddModelError("ConflitoQuantidadeQuestoesMateria", $"A quantidade inserida ultrapassa o limite de questões do teste! Atual: {quantidadeTotalQuestoes}/{testeSelecionado.QuantidadeQuestoes}");
        }

        if (!ModelState.IsValid)
        {
            List<Questao> questoes = materiaSelecionada.Questoes.ToList();

            Disciplina disciplinaSelecionada = repositorioDisciplina.SelecionarRegistroPorId(testeSelecionado.Disciplina.Id)!;

            List<Materia> materias = repositorioMateria.SelecionarRegistros()
                .Where(m => m.Disciplina.Equals(disciplinaSelecionada) && m.Serie.Equals(testeSelecionado.Serie))
                .ToList();

            DefinirQuantidadeQuestoesViewModel definirVM = new DefinirQuantidadeQuestoesViewModel
            {
                Id = testeSelecionado.Id,
                Titulo = testeSelecionado.Titulo,
                NomeDisciplina = disciplinaSelecionada.Nome,
                Serie = testeSelecionado.Serie,
                MateriaId = materiaId,
                Questoes = questoes.Select(q => new SelectListItem()
                {
                    Text = q.Enunciado,
                    Value = q.Id.ToString()
                }).ToList()
            };

            return View(definirVM);
        }

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {

            if (vm.QuantidadeQuestoesMateria == 0 && objComQuantidade is not null)
            {
                testeSelecionado.QuantidadesPorMateria.Remove(objComQuantidade);

                contexto.QuantidadesPorMateria.Remove(objComQuantidade!);
            }
            else if (objComQuantidade is not null)
            {
                objComQuantidade.QuantidadeQuestoes = vm.QuantidadeQuestoesMateria;
            }
            else
            {
                objComQuantidade = new()
                {
                    Id = Guid.NewGuid(),
                    Materia = materiaSelecionada,
                    QuantidadeQuestoes = vm.QuantidadeQuestoesMateria
                };

                testeSelecionado.QuantidadesPorMateria.Add(objComQuantidade);

                contexto.QuantidadesPorMateria.Add(objComQuantidade!);
            }

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(GerarTeste), new { id });
    }

    [HttpPost, Route("/testes/{id:guid}/aleatorizar-questoes")]
    public IActionResult AleatorizarQuestoes(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        TempData["Embaralhar"] = true;

        string tipoGeracao = testeSelecionado.EhProvao ? nameof(GerarProvao) : nameof(GerarTeste);

        return RedirectToAction(tipoGeracao, new { id });
    }

    [HttpGet("gerar-provao")]
    public IActionResult GerarProvao(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        List<Materia> materias = repositorioMateria.SelecionarRegistros()
            .Where(m => m.Disciplina.Equals(testeSelecionado.Disciplina) && m.Serie.Equals(testeSelecionado.Serie))
            .ToList();

        materias.Shuffle();

        List<Materia> materiasSelecionadas = materias;

        testeSelecionado.Questoes.Clear();
        contexto.QuantidadesPorMateria.RemoveRange(testeSelecionado.QuantidadesPorMateria);
        contexto.SaveChanges();

        foreach (Materia materia in materiasSelecionadas)
        {
            testeSelecionado.AderirMateria(materia);
        }

        if (testeSelecionado.Questoes.Count < testeSelecionado.QuantidadeQuestoes)
        {
            List<Questao> todasQuestoes = new List<Questao>();

            foreach (Materia materia in materiasSelecionadas)
            {
                List<Questao> questoesDaMateria = repositorioQuestao.SelecionarRegistros()
                    .Where(q => q.Materia.Id.Equals(materia.Id) && q.Finalizado)
                    .Take(testeSelecionado.QuantidadeQuestoes)
                    .ToList();

                todasQuestoes.AddRange(questoesDaMateria);
            }

            todasQuestoes.Shuffle();

            foreach (Questao questao in todasQuestoes.Take(testeSelecionado.QuantidadeQuestoes).ToList())
            {
                if (testeSelecionado.Questoes.Any(q => q.Equals(questao)))
                    continue;

                testeSelecionado.AderirQuestao(questao);
                repositorioTeste.AtualizarQuantidadePorMateria(testeSelecionado, questao.Materia);
            }
        }
        contexto.SaveChanges();
        testeSelecionado.Questoes.Shuffle();

        FormGerarPostViewModel formGerarPostVM = testeSelecionado.ParaGerarTestePostVM(materias, materiasSelecionadas);

        return View(formGerarPostVM);
    }

    [HttpPost("gerar-provao")]
    public IActionResult GerarProvao(Guid id, FormGerarPostViewModel formGerarPostVM)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        if (formGerarPostVM.QuestoesSelecionadasIds.Count < testeSelecionado.QuantidadeQuestoes)
            ModelState.AddModelError("ConflitoGeracao", "O número de questões selecionadas é menor do que o esperado.");

        if (!ModelState.IsValid)
        {
            List<Materia> materias = repositorioMateria.SelecionarRegistros()
                .Where(m => m.Disciplina.Id.Equals(testeSelecionado.Disciplina.Id) && m.Serie.Equals(testeSelecionado.Serie)).ToList();

            FormGerarViewModel formGerarVM = testeSelecionado.ParaGerarTestePostVM(materias, testeSelecionado.Materias);

            return View(nameof(GerarProvao), formGerarVM);
        }

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            testeSelecionado.Finalizado = true;

            contexto.Update(testeSelecionado);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duplicar/{id:guid}")]
    public IActionResult Duplicar(Guid id)
    {
        DuplicarViewModel duplicarVM = new();

        return View(duplicarVM);
    }

    [HttpPost("duplicar/{id:guid}")]
    public IActionResult Duplicar(Guid id, DuplicarViewModel duplicarVM)
    {
        if (repositorioTeste.SelecionarRegistros().Any(t => t.Titulo.Equals(duplicarVM.Titulo)))
            ModelState.AddModelError("ConflitoCadastro", "Já existe um teste com este título.");

        if (!ModelState.IsValid)
            return View(duplicarVM);

        Teste testeOriginal = repositorioTeste.SelecionarRegistroPorId(id)!;

        Teste novoTeste = new Teste()
        {
            Id = Guid.NewGuid(),
            Titulo = duplicarVM.Titulo,
            Disciplina = testeOriginal.Disciplina,
            Serie = testeOriginal.Serie,
            EhProvao = testeOriginal.EhProvao,
            QuantidadeQuestoes = testeOriginal.QuantidadeQuestoes,
            Materias = testeOriginal.Materias.ToList(),
            Questoes = new List<Questao>(),
            QuantidadesPorMateria = new List<TesteMateriaQuantidade>()
        };

        foreach (TesteMateriaQuantidade qpm in testeOriginal.QuantidadesPorMateria)
        {
            novoTeste.QuantidadesPorMateria.Add(new TesteMateriaQuantidade
            {
                Id = Guid.NewGuid(),
                Materia = qpm.Materia,
                QuantidadeQuestoes = qpm.QuantidadeQuestoes
            });
        }

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            contexto.Testes.Add(novoTeste);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch
        {
            transacao.Rollback();

            throw;
        }

        string tipoGeracao = novoTeste.EhProvao ? nameof(GerarProvao) : nameof(GerarTeste);

        return RedirectToAction(tipoGeracao, new { id = novoTeste.Id });
    }

    [HttpGet("excluir/{id:guid}")]
    public IActionResult Excluir(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        ExcluirTesteViewModel excluirVM = new ExcluirTesteViewModel(id, testeSelecionado.Titulo);

        return View(excluirVM);
    }

    [HttpPost("excluir/{id:guid}")]
    public IActionResult ExcluirConfirmado(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioTeste.ExcluirRegistro(id);

            contexto.Testes.Remove(testeSelecionado);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet, Route("/testes/{id:guid}/detalhes-teste")]
    public IActionResult DetalhesTeste(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        DetalhesTesteViewModel detalhesTesteVM = testeSelecionado.ParaDetalhesTesteVM();

        return View(detalhesTesteVM);
    }

    [HttpGet, Route("/testes/{id:guid}/detalhes-provao")]
    public IActionResult DetalhesProvao(Guid id)
    {
        Teste testeSelecionado = repositorioTeste.SelecionarRegistroPorId(id)!;

        DetalhesProvaoViewModel detalhesProvaoVM = testeSelecionado.ParaDetalhesProvaoVM();

        return View(detalhesProvaoVM);
    }

    [HttpGet, Route("/testes/{id:guid}/gerar-pdf")]
    public IActionResult GerarPdf(Guid id)
    {
        Teste teste = repositorioTeste.SelecionarRegistroPorId(id)!;

        byte[] pdfBytes = geradorPdfService.GerarPdfTeste(teste);

        return File(pdfBytes, "application/pdf");
    }

    [HttpGet, Route("/testes/{id:guid}/gerar-gabarito")]
    public IActionResult GerarGabaritoPdf(Guid id)
    {
        Teste teste = repositorioTeste.SelecionarRegistroPorId(id)!;

        byte[] pdfBytes = geradorPdfService.GerarPdfGabarito(teste);

        return File(pdfBytes, "application/pdf");
    }
}
