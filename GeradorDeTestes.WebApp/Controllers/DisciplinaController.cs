using GeradorDeTestes.Dominio.ModuloDisciplina;
using GeradorDeTestes.Dominio.ModuloMateria;
using GeradorDeTestes.Dominio.ModuloTeste;
using GeradorDeTestes.Infraestrutura.ORM.Compartilhado;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeradorDeTestes.WebApp.Controllers;

[Route("disciplinas")]
public class DisciplinaController : Controller
{
    private readonly GeradorDeTestesDbContext contexto;
    private readonly IRepositorioDisciplina repositorioDisciplina;
    private readonly IRepositorioMateria repositorioMateria;
    private readonly IRepositorioTeste repositorioTeste;

    public DisciplinaController(GeradorDeTestesDbContext contexto, IRepositorioDisciplina repositorioDisciplina,
        IRepositorioMateria repositorioMateria, IRepositorioTeste repositorioTeste)
    {
        this.contexto = contexto;
        this.repositorioDisciplina = repositorioDisciplina;
        this.repositorioMateria = repositorioMateria;
        this.repositorioTeste = repositorioTeste;
    }

    [HttpGet]
    public IActionResult Index()
    {
        List<Disciplina> disciplinas = repositorioDisciplina.SelecionarRegistros();

        VisualizarDisciplinasViewModel visualizarVM = new()
        {
            Registros = disciplinas.Select(d => d.ParaDetalhesVM()).ToList()
        };

        return View(visualizarVM);
    }

    [HttpGet("cadastrar")]
    public IActionResult Cadastrar()
    {
        return View(new CadastrarDisciplinaViewModel());
    }

    [HttpPost("cadastrar")]
    public IActionResult Cadastrar(CadastrarDisciplinaViewModel cadastrarVM)
    {
        List<Disciplina> disciplinas = repositorioDisciplina.SelecionarRegistros();

        if (disciplinas.Any(d => d.Nome.Equals(cadastrarVM.Nome)))
            ModelState.AddModelError("ConflitoCadastro", "Já existe uma disciplina com este nome.");

        if (!ModelState.IsValid)
            return View(cadastrarVM);

        Disciplina novaDisciplina = cadastrarVM.ParaEntidade();

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioDisciplina.CadastrarRegistro(novaDisciplina);

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

    [HttpGet("editar/{id}")]
    public IActionResult Editar(Guid id)
    {
        Disciplina disciplinaSelecionada = repositorioDisciplina.SelecionarRegistroPorId(id)!;

        EditarDisciplinaViewModel editarVM = new()
        {
            Id = disciplinaSelecionada.Id,
            Nome = disciplinaSelecionada.Nome
        };

        return View(editarVM);
    }

    [HttpPost("editar/{id}")]
    public IActionResult Editar(Guid id, EditarDisciplinaViewModel editarVM)
    {
        List<Disciplina> disciplinas = repositorioDisciplina.SelecionarRegistros();

        if (disciplinas.Any(d => d.Nome.Equals(editarVM.Nome) && d.Id != id))
            ModelState.AddModelError("ConflitoEdicao", "Já existe uma disciplina com este nome.");

        if (!ModelState.IsValid)
            return View(editarVM);

        Disciplina disciplinaSelecionada = disciplinas.FirstOrDefault(d => d.Nome.Equals(editarVM.Nome) && d.Id != id)!;

        Disciplina disciplinaEditada = editarVM.ParaEntidade();

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioDisciplina.EditarRegistro(id, disciplinaEditada);

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

    [HttpGet("excluir/{id}")]
    public IActionResult Excluir(Guid id)
    {
        Disciplina disciplinaSelecionada = repositorioDisciplina.SelecionarRegistroPorId(id)!;

        ExcluirDisciplinaViewModel excluirVM = new()
        {
            Id = disciplinaSelecionada.Id,
            Nome = disciplinaSelecionada.Nome
        };

        return View(excluirVM);
    }

    [HttpPost("excluir/{id}")]
    public IActionResult ExcluirConfirmado(Guid id)
    {
        Disciplina disciplinaSelecionada = repositorioDisciplina.SelecionarRegistroPorId(id)!;
        List<Materia> materias = repositorioMateria.SelecionarRegistros();
        List<Teste> testes = repositorioTeste.SelecionarRegistros();

        if (materias.Any(m => m.Disciplina.Id.Equals(id)) || testes.Any(t => t.Disciplina.Id.Equals(id)))
        {
            ModelState.AddModelError("ConflitoExclusao", "Não é possível excluir: há matérias ou testes vinculados.");

            ExcluirDisciplinaViewModel excluirVM = new()
            {
                Id = disciplinaSelecionada.Id,
                Nome = disciplinaSelecionada.Nome
            };

            return View("Excluir", excluirVM);
        }

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioDisciplina.ExcluirRegistro(id);

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

    [HttpGet("detalhes/{id}")]
    public IActionResult Detalhes(Guid id)
    {
        Disciplina disciplinaSelecionada = repositorioDisciplina.SelecionarRegistroPorId(id)!;

        DetalhesDisciplinaViewModel detalhesVM = disciplinaSelecionada.ParaDetalhesVM();

        return View(detalhesVM);
    }
}
